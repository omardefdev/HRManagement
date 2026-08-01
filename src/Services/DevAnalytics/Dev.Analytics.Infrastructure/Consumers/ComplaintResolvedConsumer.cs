using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Services;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dev.Analytics.Infrastructure.Consumers;

public class ComplaintResolvedConsumer : IConsumer<IComplaintResolved>
{
    private readonly AnalyticsDbContext _db;
    private readonly ILogger<ComplaintResolvedConsumer> _logger;

    public ComplaintResolvedConsumer(AnalyticsDbContext db, ILogger<ComplaintResolvedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IComplaintResolved> context)
    {
        var m = context.Message;
        var referenceKey = m.ComplaintId.ToString("N");
        var idem = AnalyticsProjectionRules.IdempotencyKey("ComplaintResolved", referenceKey);

        if (await _db.AnalyticsEvents.AnyAsync(e => e.IdempotencyKey == idem, context.CancellationToken))
            return;

        _logger.LogInformation(
            "Dev.Analytics: ComplaintResolved ComplaintId={ComplaintId}",
            m.ComplaintId);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            referenceKey = $"ComplaintResolved:{referenceKey}",
            m.ComplaintId,
            m.Subject,
            m.Resolution,
            m.ResolvedAt,
            m.SourceSystem,
            m.TenantId
        });

        _db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            EventName = "ComplaintResolved",
            Payload = payload,
            OccurredAt = m.ResolvedAt == default ? DateTime.UtcNow : m.ResolvedAt,
            OrgUnitId = null,
            IdempotencyKey = idem
        });
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}

public class CardIssuedAnalyticsConsumer : IConsumer<ICardIssued>
{
    private readonly AnalyticsDbContext _db;
    private readonly ILogger<CardIssuedAnalyticsConsumer> _logger;

    public CardIssuedAnalyticsConsumer(AnalyticsDbContext db, ILogger<CardIssuedAnalyticsConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ICardIssued> context)
    {
        var m = context.Message;
        var referenceKey = m.IssuedCardId.ToString("N");
        var idem = AnalyticsProjectionRules.IdempotencyKey("CardIssued", referenceKey);

        if (await _db.AnalyticsEvents.AnyAsync(e => e.IdempotencyKey == idem, context.CancellationToken))
            return;

        _logger.LogInformation(
            "Dev.Analytics: CardIssued EmployeeId={EmployeeId} CardId={CardId}",
            m.EmployeeId, m.IssuedCardId);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            referenceKey = $"CardIssued:{referenceKey}",
            m.EmployeeId,
            m.IssuedCardId,
            m.SerialNumber,
            m.IssuedAt
        });

        _db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            EventName = "CardIssued",
            Payload = payload,
            OccurredAt = m.IssuedAt == default ? DateTime.UtcNow : m.IssuedAt,
            OrgUnitId = null,
            IdempotencyKey = idem
        });
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
