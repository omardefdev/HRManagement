using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Services;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dev.Analytics.Infrastructure.Consumers;

public class SeparationCompletedConsumer : IConsumer<ISeparationCompleted>
{
    private readonly AnalyticsDbContext _db;
    private readonly ILogger<SeparationCompletedConsumer> _logger;

    public SeparationCompletedConsumer(AnalyticsDbContext db, ILogger<SeparationCompletedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ISeparationCompleted> context)
    {
        var m = context.Message;
        var referenceKey = m.SeparationRecordId.HasValue
            ? $"{m.EmployeeId:N}:{m.SeparationRecordId:N}"
            : m.EmployeeId.ToString("N");
        var idem = AnalyticsProjectionRules.IdempotencyKey("SeparationCompleted", referenceKey);

        if (await _db.AnalyticsEvents.AnyAsync(e => e.IdempotencyKey == idem, context.CancellationToken))
            return;

        _logger.LogInformation(
            "Dev.Analytics: SeparationCompleted EmployeeId={EmployeeId}",
            m.EmployeeId);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            referenceKey = $"SeparationCompleted:{referenceKey}",
            m.EmployeeId,
            m.SeparationType,
            m.EffectiveDate,
            m.SourceSystem,
            m.TenantId
        });

        _db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            EventName = "SeparationCompleted",
            Payload = payload,
            OccurredAt = m.OccurredAt == default ? DateTime.UtcNow : m.OccurredAt,
            OrgUnitId = null,
            IdempotencyKey = idem
        });
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
