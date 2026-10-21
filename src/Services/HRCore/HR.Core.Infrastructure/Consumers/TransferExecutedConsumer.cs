using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Legal;

namespace HR.Core.Infrastructure.Consumers;

/// <summary>SYR-008: applies transfer with service regime continuity.</summary>
public sealed class TransferExecutedConsumer(
    HRCoreDbContext db,
    ServiceRegimeTransitionService transitionService,
    ServiceRegimeExceptionService exceptionService,
    ILogger<TransferExecutedConsumer> logger) : IConsumer<ITransferExecuted>
{
    public async Task Consume(ConsumeContext<ITransferExecuted> context)
    {
        var m = context.Message;
        logger.LogInformation(
            "HR.Core: TransferExecuted EmployeeId={EmployeeId} ToOrgUnitId={ToOrgUnitId} FromRegime={FromRegime} ToRegime={ToRegime}",
            m.EmployeeId, m.ToOrgUnitId, m.FromLegalRegimeId, m.ToLegalRegimeId);

        var emp = await db.Employees.FirstOrDefaultAsync(e => e.Id == m.EmployeeId, context.CancellationToken);
        if (emp is null)
            return;

        if (emp.PrimaryOrgUnitId == m.ToOrgUnitId)
            return;

        var openRecords = await db.EmploymentRecords
            .Where(r => r.EmployeeId == m.EmployeeId && r.EndDate == null)
            .ToListAsync(context.CancellationToken);

        var priorRegime = openRecords.FirstOrDefault()?.LegalRegimeId ?? m.FromLegalRegimeId;
        var targetRegime = ResolveTargetRegime(m, priorRegime);

        var endDate = m.ExecutedAt.Date;
        EmploymentRecord? closedRecord = null;
        foreach (var record in openRecords)
        {
            record.EndDate = endDate;
            record.Status = "closed";
            closedRecord ??= record;
        }

        emp.PrimaryOrgUnitId = m.ToOrgUnitId;
        var note = $"TransferExecuted FromOrg={m.FromOrgUnitId} ToOrg={m.ToOrgUnitId} At={m.ExecutedAt:O}";

        if (IsCrossRegime(priorRegime, targetRegime))
        {
            var transitionError = await transitionService.ApplyTransitionAsync(
                m.EmployeeId,
                targetRegime!,
                m.TransitionDecisionRef ?? string.Empty,
                m.ConditionCode,
                m.ExecutedAt,
                appliedBy: null,
                closedRecord?.Id,
                m.ConflictNotes,
                context.CancellationToken);

            if (transitionError is not null)
            {
                logger.LogWarning(
                    "Transfer regime transition failed for employee {EmployeeId}: {Error}",
                    m.EmployeeId,
                    transitionError);
                targetRegime = priorRegime;
            }
        }

        var activeRegime = IsCrossRegime(priorRegime, targetRegime) && targetRegime is not null
            ? targetRegime
            : priorRegime;

        var newRecord = new EmploymentRecord
        {
            EmployeeId = m.EmployeeId,
            PositionTitle = "نقل وظيفي",
            OrgUnitId = m.ToOrgUnitId,
            StartDate = m.ExecutedAt.Date,
            EmploymentType = HRManagement.SharedKernel.Enums.EmploymentType.Permanent,
            Notes = note,
            LegalRegimeId = activeRegime
        };
        db.EmploymentRecords.Add(newRecord);

        if (string.IsNullOrWhiteSpace(activeRegime))
        {
            await exceptionService.EnqueueIfUnclassifiedAsync(
                emp,
                newRecord,
                ServiceRegimeExceptionSource.Transition,
                legalSourceId: null,
                attemptedRegimeId: targetRegime,
                context.CancellationToken);
        }
        else if (IsCrossRegime(priorRegime, targetRegime)
                 && ServiceRegimeArticle159Rules.RequiresLegalDecision(priorRegime, targetRegime))
        {
            var conflict = ServiceRegimeArticle159Rules.DetectConflict(priorRegime, targetRegime!);
            if (conflict is not null)
            {
                await exceptionService.EnqueueArticle159Async(
                    m.EmployeeId,
                    closedRecord?.Id ?? newRecord.Id,
                    priorRegime ?? conflict.FromRegimeId,
                    targetRegime!,
                    conflict.EntitlementsJson,
                    ServiceRegimeExceptionSource.Transition,
                    context.CancellationToken);
            }
        }

        await SeparationCompletedConsumer.AppendApprovedCvNoteAsync(db, m.EmployeeId, note, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    private static string? ResolveTargetRegime(ITransferExecuted message, string? priorRegime)
    {
        if (!string.IsNullOrWhiteSpace(message.ToLegalRegimeId))
            return message.ToLegalRegimeId;

        return priorRegime;
    }

    private static bool IsCrossRegime(string? fromRegime, string? toRegime) =>
        !string.IsNullOrWhiteSpace(toRegime)
        && !string.Equals(fromRegime, toRegime, StringComparison.OrdinalIgnoreCase);
}
