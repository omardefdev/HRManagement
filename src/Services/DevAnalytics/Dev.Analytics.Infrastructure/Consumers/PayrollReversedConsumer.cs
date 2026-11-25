using System.Text.Json;
using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Services;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dev.Analytics.Infrastructure.Consumers;

/// <summary>
/// PLAN-56 (G-PAY-06): records the reversal of a posted payroll run beside its <c>PayrollProcessed</c> event, with the amount it
/// takes back (as a negative effect), so that a reader of the two events sees the run's net effect. Idempotent per run: a run is
/// reversed once, and a redelivered message adds nothing.
/// </summary>
public class PayrollReversedConsumer : IConsumer<IPayrollReversed>
{
    public const string EventName = "PayrollReversed";

    private readonly AnalyticsDbContext _db;
    private readonly ILogger<PayrollReversedConsumer> _logger;

    public PayrollReversedConsumer(AnalyticsDbContext db, ILogger<PayrollReversedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IPayrollReversed> context)
    {
        var m = context.Message;
        _logger.LogInformation(
            "Dev.Analytics: PayrollReversed PayrollRunId={PayrollRunId}",
            m.PayrollRunId);

        var idem = AnalyticsProjectionRules.IdempotencyKey(EventName, m.PayrollRunId.ToString("N"));
        if (await _db.AnalyticsEvents.AnyAsync(e => e.IdempotencyKey == idem, context.CancellationToken))
            return;

        var payload = JsonSerializer.Serialize(new
        {
            m.PayrollRunId,
            m.PeriodStart,
            m.PeriodEnd,
            m.ReversedNetPay,
            // The run's effect on a total of posted pay: what the posting added, taken back.
            NetEffect = -m.ReversedNetPay,
            m.ReversalRequestId,
            m.ReversedAt,
            m.TenantId
        });

        _db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            EventName = EventName,
            Payload = payload,
            OccurredAt = m.ReversedAt,
            OrgUnitId = null,
            IdempotencyKey = idem
        });
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
