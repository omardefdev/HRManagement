using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Consumers;

/// <summary>SYR-008: records loan assignment without changing service regime.</summary>
public sealed class LoanAssignmentExecutedConsumer(
    HRCoreDbContext db,
    ILogger<LoanAssignmentExecutedConsumer> logger) : IConsumer<ILoanAssignmentExecuted>
{
    public async Task Consume(ConsumeContext<ILoanAssignmentExecuted> context)
    {
        var m = context.Message;
        logger.LogInformation(
            "HR.Core: LoanAssignmentExecuted EmployeeId={EmployeeId} Lending={Lending} Borrowing={Borrowing}",
            m.EmployeeId,
            m.LendingOrgUnitId,
            m.BorrowingOrgUnitId);

        var record = await db.EmploymentRecords
            .Where(r => r.EmployeeId == m.EmployeeId && r.EndDate == null)
            .OrderByDescending(r => r.StartDate)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (record is null)
            return;

        var note =
            $"LoanAssignmentExecuted Lending={m.LendingOrgUnitId} Borrowing={m.BorrowingOrgUnitId} Start={m.StartDate:O} End={m.EndDate:O}";
        record.Notes = string.IsNullOrWhiteSpace(record.Notes)
            ? note
            : $"{record.Notes}\n{note}";

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
