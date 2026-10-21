using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Consumers;

/// <summary>SYR-008: records secondment without changing service regime.</summary>
public sealed class SecondmentExecutedConsumer(
    HRCoreDbContext db,
    ILogger<SecondmentExecutedConsumer> logger) : IConsumer<ISecondmentExecuted>
{
    public async Task Consume(ConsumeContext<ISecondmentExecuted> context)
    {
        var m = context.Message;
        logger.LogInformation(
            "HR.Core: SecondmentExecuted EmployeeId={EmployeeId} HostOrg={HostOrg}",
            m.EmployeeId,
            m.HostOrg);

        var record = await db.EmploymentRecords
            .Where(r => r.EmployeeId == m.EmployeeId && r.EndDate == null)
            .OrderByDescending(r => r.StartDate)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (record is null)
            return;

        var note = $"SecondmentExecuted HostOrg={m.HostOrg} Start={m.StartDate:O} End={m.EndDate:O}";
        record.Notes = string.IsNullOrWhiteSpace(record.Notes)
            ? note
            : $"{record.Notes}\n{note}";

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
