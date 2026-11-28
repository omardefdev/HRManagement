using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Sagas;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Contracts.Infrastructure.Consumers;

public class ProcessInterventionRequestedConsumer(
    ContractsDbContext db,
    ILogger<ProcessInterventionRequestedConsumer> logger) : IConsumer<IProcessInterventionRequested>
{
    public async Task Consume(ConsumeContext<IProcessInterventionRequested> context)
    {
        var m = context.Message;
        if (m.SagaType is not (SagaTypeNames.Separation or SagaTypeNames.Hire))
            return;

        var exists = await db.ProcessInterventions.AnyAsync(
            i => i.SagaType == m.SagaType
                 && i.CorrelationId == m.CorrelationId
                 && i.Status == ProcessInterventionStatuses.Open,
            context.CancellationToken);
        if (exists)
            return;

        db.ProcessInterventions.Add(new ProcessIntervention
        {
            SagaType = m.SagaType,
            CorrelationId = m.CorrelationId,
            Reason = m.Reason,
            FailedParty = m.FailedParty,
            Status = ProcessInterventionStatuses.Open,
            TenantId = m.TenantId ?? Guid.Empty
        });
        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogWarning(
            "Process intervention opened {SagaType} {CorrelationId}: {Reason}",
            m.SagaType, m.CorrelationId, m.Reason);
    }
}

/// <summary>
/// Completes and archives the separation once every saga party has succeeded.
/// <para>
/// Archival used to happen the moment the saga was published, which declared the separation
/// finished before any party had acknowledged. A party failing afterwards then left an archived
/// record of work that never completed. Archival now depends on the saga's own success signal.
/// </para>
/// </summary>
public class SeparationSagaFinalizedConsumer(
    ContractsDbContext db,
    IContractsRepository repository,
    ILogger<SeparationSagaFinalizedConsumer> logger) : IConsumer<ISeparationSagaFinalized>
{
    public const string CompletedReason = "اكتملت رحلة الفصل: أقرّت جميع الأطراف الإلزامية (النظام)";

    /// <summary>
    /// PLAN-60 (§5): a successful saga completes the record — <c>Executing</c> (or <c>AwaitingIntervention</c> resumed) → <c>Completed</c> —
    /// and its own transaction, found by <c>SeparationRecordId</c>, follows. The saga's row is the parties' truth: an item of a party it
    /// counts as done is completed here if its acknowledgement has not been stamped yet, so <c>Completed</c> always means every mandatory
    /// party acknowledged. The actor is the system (null), named in the reason.
    /// </summary>
    public async Task Consume(ConsumeContext<ISeparationSagaFinalized> context)
    {
        var message = context.Message;
        if (!message.Success)
        {
            // Compensated or abandoned: the record stays where it is, visible (PLAN-63 owns the compensation).
            return;
        }

        var record = await db.SeparationRecords.FirstOrDefaultAsync(r => r.Id == message.SeparationRecordId, context.CancellationToken);
        if (record is null || record.Status == SeparationStatus.Completed)
            return;
        if (record.Status is not (SeparationStatus.Executing or SeparationStatus.AwaitingIntervention))
        {
            logger.LogWarning("Separation {RecordId} finalized while {Status}; left as it is.", record.Id, record.Status);
            return;
        }

        var saga = await db.SeparationSagaStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SeparationRecordId == record.Id, context.CancellationToken);
        var acknowledged = (saga?.CompletedParties ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var items = await db.SeparationChecklists
            .Where(c => c.SeparationRecordId == record.Id && c.CompletionMode == SeparationStepCompletionMode.PartyAck && !c.IsCompleted)
            .ToListAsync(context.CancellationToken);
        foreach (var item in items.Where(i => acknowledged.Contains(i.PartyName, StringComparer.OrdinalIgnoreCase)))
        {
            item.IsCompleted = true;
            item.CompletedAt = DateTime.UtcNow;
            item.SourceSystem = item.PartyName;
            item.EvidenceRef ??= $"saga:{item.PartyName}";
        }

        if (record.Status == SeparationStatus.AwaitingIntervention)
            await SeparationStatusSync.TransitionAsync(
                repository, record, SeparationStatus.Executing, null, "استئناف آلي عند اكتمال الأطراف (النظام)", context.CancellationToken);
        await SeparationStatusSync.TransitionAsync(
            repository, record, SeparationStatus.Completed, null, CompletedReason, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>
/// PLAN-60 (§4): a party's acknowledgement of the execution completes its own PartyAck item — IsCompleted, the party's evidence, the party
/// as the source, no person (CompletedBy null is allowed here only). It generalises FUN-002-B's PayrollSettled stamp to the seven parties.
/// Only while the record is executing (or waiting for an intervention) and its saga is not compensating: a compensation's acknowledgement
/// completes nothing.
/// </summary>
public class SeparationPartyAckChecklistConsumer(
    ContractsDbContext db,
    ILogger<SeparationPartyAckChecklistConsumer> logger) : IConsumer<ISeparationPartyCompleted>
{
    public async Task Consume(ConsumeContext<ISeparationPartyCompleted> context)
    {
        var m = context.Message;
        if (SeparationChecklistDefaults.StepForParty(m.Party) is not SeparationStep step)
            return;

        var record = await db.SeparationRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == m.SeparationRecordId, context.CancellationToken);
        if (record is null || record.Status is not (SeparationStatus.Executing or SeparationStatus.AwaitingIntervention))
            return;

        var saga = await db.SeparationSagaStates.AsNoTracking()
            .Where(s => s.SeparationRecordId == m.SeparationRecordId)
            .Select(s => s.CurrentState)
            .FirstOrDefaultAsync(context.CancellationToken);
        if (saga is "Compensating" or "Cancelled")
        {
            logger.LogInformation("Separation {RecordId}: {Party}'s acknowledgement of a compensation completes no item.", m.SeparationRecordId, m.Party);
            return;
        }

        var item = await db.SeparationChecklists.FirstOrDefaultAsync(
            c => c.SeparationRecordId == m.SeparationRecordId && c.Step == step, context.CancellationToken);
        if (item is null || item.IsCompleted || item.CompletionMode != SeparationStepCompletionMode.PartyAck)
            return;

        item.IsCompleted = true;
        item.CompletedAt = DateTime.UtcNow;
        item.CompletedBy = null;
        item.EvidenceRef = string.IsNullOrWhiteSpace(m.EvidenceRef) ? $"ack:{m.Party}" : m.EvidenceRef;
        item.SourceSystem = m.Party;
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>INT-003: Contracts party — marks checklist mirror from saga ACK path for ContractTerminated evidence already done pre-start.</summary>
public class ContractsSeparationPartyConsumer(
    ContractsDbContext db,
    ILogger<ContractsSeparationPartyConsumer> logger) : IConsumer<IExecuteSeparationParty>
{
    public async Task Consume(ConsumeContext<IExecuteSeparationParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, ProcessParties.Contracts, StringComparison.OrdinalIgnoreCase))
            return;

        if (m.Compensate)
        {
            // PLAN-60: the record's status follows its own transitions; undoing an execution is PLAN-63's compensation.
            await context.Publish<ISeparationPartyCompleted>(new
            {
                m.SeparationRecordId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = "contracts:compensate-unpublished",
                m.CorrelationId,
                m.TenantId
            });
            return;
        }

        logger.LogInformation("Contracts separation party ACK for {Id}", m.SeparationRecordId);
        await context.Publish<ISeparationPartyCompleted>(new
        {
            m.SeparationRecordId,
            m.EmployeeId,
            m.Party,
            EvidenceRef = "contracts:ack",
            m.CorrelationId,
            m.TenantId
        });
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>Archives service transactions once Records has persisted the archive item.</summary>
public class TransactionArchivedConsumer(ContractsDbContext db) : IConsumer<ITransactionArchived>
{
    public async Task Consume(ConsumeContext<ITransactionArchived> context)
    {
        var message = context.Message;
        var tx = await db.ServiceTransactions.FindAsync([message.TransactionId], context.CancellationToken);
        if (tx is null)
            return;

        if (tx.State == ServiceTransactionState.Completed)
            tx.MoveTo(ServiceTransactionState.Archived, null, $"Archived: {message.ArchiveRef}");

        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>Moves hire transactions to Archived once every hire party has succeeded.</summary>
public class HireCaseCompletedConsumer(ContractsDbContext db) : IConsumer<IHireCaseCompleted>
{
    public async Task Consume(ConsumeContext<IHireCaseCompleted> context)
    {
        var message = context.Message;
        var tx = await db.ServiceTransactions.FindAsync([message.ServiceTransactionId], context.CancellationToken);
        if (tx is null)
            return;

        if (tx.State == ServiceTransactionState.Executing)
            tx.MoveTo(ServiceTransactionState.PostProcessing, null, null);
        if (tx.State == ServiceTransactionState.PostProcessing)
            tx.MoveTo(ServiceTransactionState.Completed, null, "All hire parties completed");
        if (tx.State == ServiceTransactionState.Completed)
            tx.MoveTo(ServiceTransactionState.Archived, null, "Hire archived after saga completed");

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
