using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Services;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dev.Analytics.Infrastructure.Consumers;

public class EmployeeCreatedConsumer : IConsumer<IEmployeeCreated>
{
    private readonly AnalyticsDbContext _db;
    private readonly ILogger<EmployeeCreatedConsumer> _logger;

    public EmployeeCreatedConsumer(AnalyticsDbContext db, ILogger<EmployeeCreatedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IEmployeeCreated> context)
    {
        var m = context.Message;
        var referenceKey = m.EmployeeId.ToString("N");
        var idem = AnalyticsProjectionRules.IdempotencyKey("EmployeeCreated", referenceKey);

        if (await _db.AnalyticsEvents.AnyAsync(e => e.IdempotencyKey == idem, context.CancellationToken))
            return;

        _logger.LogInformation(
            "Dev.Analytics: EmployeeCreated EmployeeId={EmployeeId} Org={Org}",
            m.EmployeeId, m.PrimaryOrgUnitId);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            referenceKey = $"EmployeeCreated:{referenceKey}",
            m.EmployeeId,
            m.FullName,
            // Correlation only. Analytics has no need for the national identifier and storing it
            // here put it in a fourth database with none of HR Core's protections.
            m.NationalIdHash,
            m.EmploymentType,
            m.TenantId,
            m.PrimaryOrgUnitId
        });

        _db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            EventName = "EmployeeCreated",
            Payload = payload,
            OccurredAt = DateTime.UtcNow,
            OrgUnitId = m.PrimaryOrgUnitId,
            IdempotencyKey = idem
        });
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
