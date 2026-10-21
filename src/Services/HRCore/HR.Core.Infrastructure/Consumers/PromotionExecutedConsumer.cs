using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Consumers;

/// <summary>SYR-009: records promotion metadata without EST-001 grade refactor.</summary>
public sealed class PromotionExecutedConsumer(
    HRCoreDbContext db,
    ILogger<PromotionExecutedConsumer> logger) : IConsumer<IPromotionExecuted>
{
    public async Task Consume(ConsumeContext<IPromotionExecuted> context)
    {
        var m = context.Message;
        logger.LogInformation(
            "HR.Core: PromotionExecuted EmployeeId={EmployeeId} {FromGrade}/{FromStep} -> {ToGrade}/{ToStep}",
            m.EmployeeId,
            m.FromGrade,
            m.FromStep,
            m.ToGrade,
            m.ToStep);

        var record = await db.EmploymentRecords
            .Where(r => r.EmployeeId == m.EmployeeId && r.EndDate == null)
            .OrderByDescending(r => r.StartDate)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (record is null)
            return;

        var bonusCode = ServiceRegimePromotionRules.GetBonusCode(record.LegalRegimeId ?? m.SourceLegalRegimeId);
        var note =
            $"PromotionExecuted {m.FromGrade}/{m.FromStep}->{m.ToGrade}/{m.ToStep} Decision={m.TransitionDecisionRef}"
            + (bonusCode is null ? "" : $" BonusCode={bonusCode}");

        record.Notes = string.IsNullOrWhiteSpace(record.Notes)
            ? note
            : $"{record.Notes}\n{note}";

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
