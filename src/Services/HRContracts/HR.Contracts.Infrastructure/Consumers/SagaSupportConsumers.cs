using HR.Contracts.Domain.Enums;
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
public class SeparationSagaFinalizedConsumer(ContractsDbContext db) : IConsumer<ISeparationSagaFinalized>
{
    public async Task Consume(ConsumeContext<ISeparationSagaFinalized> context)
    {
        var message = context.Message;
        var record = await db.SeparationRecords.FirstOrDefaultAsync(
            r => r.Id == message.SeparationRecordId, context.CancellationToken);
        if (record is null)
            return;

        if (!message.Success)
        {
            // Compensated or abandoned: leave the transaction where the saga left it so the
            // failure stays visible instead of being archived away.
            return;
        }

        record.SeparationEventPublished = true;

        var tx = await db.ServiceTransactions
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(
                x => x.EmployeeId == record.EmployeeId
                     && x.TransactionType == ServiceTransactionType.Separation,
                context.CancellationToken);

        if (tx is not null)
        {
            if (tx.State == ServiceTransactionState.Executing)
                tx.MoveTo(ServiceTransactionState.PostProcessing, null, null);
            if (tx.State == ServiceTransactionState.PostProcessing)
                tx.MoveTo(ServiceTransactionState.Completed, null, "All separation parties completed");
            if (tx.State == ServiceTransactionState.Completed)
                tx.MoveTo(ServiceTransactionState.Archived, null, "Separation archived after saga finalized");
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>FUN-002-B: stamp PayrollSettled checklist evidence from Payroll party ACK.</summary>
public class SeparationPartyCompletedChecklistConsumer(ContractsDbContext db) : IConsumer<ISeparationPartyCompleted>
{
    public async Task Consume(ConsumeContext<ISeparationPartyCompleted> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, ProcessParties.Payroll, StringComparison.OrdinalIgnoreCase))
            return;
        if (string.IsNullOrWhiteSpace(m.EvidenceRef))
            return;

        var step = await db.SeparationChecklists.FirstOrDefaultAsync(
            c => c.SeparationRecordId == m.SeparationRecordId
                 && c.Step == HR.Contracts.Domain.Enums.SeparationStep.PayrollSettled,
            context.CancellationToken);
        if (step is null)
            return;

        step.EvidenceRef = m.EvidenceRef;
        if (string.IsNullOrWhiteSpace(step.SourceSystem))
            step.SourceSystem = "PayrollParty";
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
            var record = await db.SeparationRecords.FindAsync([m.SeparationRecordId], context.CancellationToken);
            if (record is not null)
                record.SeparationEventPublished = false;

            await db.SaveChangesAsync(context.CancellationToken);
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
