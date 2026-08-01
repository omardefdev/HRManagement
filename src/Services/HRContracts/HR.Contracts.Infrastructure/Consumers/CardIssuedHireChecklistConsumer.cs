using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Contracts.Infrastructure.Consumers;

public class CardIssuedHireChecklistConsumer : IConsumer<ICardIssued>
{
    private readonly ContractsDbContext _db;
    private readonly ILogger<CardIssuedHireChecklistConsumer> _logger;

    public CardIssuedHireChecklistConsumer(ContractsDbContext db, ILogger<CardIssuedHireChecklistConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ICardIssued> context)
    {
        var m = context.Message;
        var txn = await _db.ServiceTransactions
            .Where(t => t.EmployeeId == m.EmployeeId
                        && t.TransactionType == ServiceTransactionType.Appointment
                        && t.State != ServiceTransactionState.Archived
                        && t.State != ServiceTransactionState.Cancelled)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (txn is null)
            return;

        var item = await _db.HireChecklists
            .FirstOrDefaultAsync(
                c => c.ServiceTransactionId == txn.Id && c.Step == HireStep.IdentityIssued,
                context.CancellationToken);

        if (item is null || item.IsCompleted)
            return;

        HireWorkflowRules.MarkStepCompleted(item, "بطاقة مُصدَرة");
        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Marked IdentityIssued hire step for employee {EmployeeId}", m.EmployeeId);
    }
}
