using System.Text.Json;
using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Services;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dev.Analytics.Infrastructure.Consumers;

public class PayrollProcessedConsumer : IConsumer<IPayrollProcessed>
{
    private readonly AnalyticsDbContext _db;
    private readonly ILogger<PayrollProcessedConsumer> _logger;

    public PayrollProcessedConsumer(AnalyticsDbContext db, ILogger<PayrollProcessedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IPayrollProcessed> context)
    {
        var m = context.Message;
        _logger.LogInformation(
            "Dev.Analytics: PayrollProcessed PayrollRunId={PayrollRunId}",
            m.PayrollRunId);

        var idem = AnalyticsProjectionRules.IdempotencyKey("PayrollProcessed", m.PayrollRunId.ToString("N"));
        if (await _db.AnalyticsEvents.AnyAsync(e => e.IdempotencyKey == idem, context.CancellationToken))
            return;

        var payload = JsonSerializer.Serialize(new
        {
            m.PayrollRunId,
            m.PeriodStart,
            m.PeriodEnd,
            m.TotalNetPay,
            m.TenantId
        });

        _db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            EventName = "PayrollProcessed",
            Payload = payload,
            OccurredAt = DateTime.UtcNow,
            OrgUnitId = null,
            IdempotencyKey = idem
        });
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
