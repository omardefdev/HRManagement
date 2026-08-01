using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Enums;
using IT.Infra.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IT.Infra.Infrastructure.Consumers;

// INT-002: inbox handled by InboxConsumeFilter
public class SeparationCompletedConsumer : IConsumer<ISeparationCompleted>
{
    private readonly ITInfraDbContext _db;
    private readonly ILogger<SeparationCompletedConsumer> _logger;

    public SeparationCompletedConsumer(ITInfraDbContext db, ILogger<SeparationCompletedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ISeparationCompleted> context)
    {
        var m = context.Message;
        if (!EventSchemaVersion.Supports(m.SchemaVersion, supportedMax: 2, _logger, nameof(ISeparationCompleted)))
            return;

        var marker = ConsumerMessageMarkers.SeparationCompleted(m.EmployeeId, m.SeparationRecordId);
        if (await _db.ITSupportTickets.AnyAsync(t =>
                t.Description != null && t.Description.Contains(marker), context.CancellationToken))
            return;

        _logger.LogInformation(
            "IT.Infra: SeparationCompleted EmployeeId={EmployeeId}, revoking permissions + detaching devices",
            m.EmployeeId);

        var openPermissions = await _db.SystemPermissionRecords
            .Where(p => p.UserId == m.EmployeeId && p.RevokedAt == null)
            .ToListAsync(context.CancellationToken);

        foreach (var p in openPermissions)
            p.RevokedAt = DateTime.UtcNow;

        var devices = await _db.UserDevices
            .Where(d => d.AssignedTo == m.EmployeeId)
            .ToListAsync(context.CancellationToken);
        foreach (var d in devices)
            d.AssignedTo = null;

        // Visible residual access marker if permissions remain unexpectedly (should be empty after revoke).
        var residual = await _db.SystemPermissionRecords
            .CountAsync(p => p.UserId == m.EmployeeId && p.RevokedAt == null, context.CancellationToken);

        _db.ITSupportTickets.Add(new ITSupportTicket
        {
            Id = Guid.NewGuid(),
            IssueSummary = $"فصل أجهزة وصلاحيات بعد الفصل — {m.EmployeeId}",
            Description = $"{marker} revoked {openPermissions.Count} permission(s); detached {devices.Count} device(s)",
            Status = residual > 0 ? TicketStatus.Open : TicketStatus.Closed,
            ReportedBy = m.EmployeeId,
            ProvisioningStatus = residual > 0 ? ProvisioningStatus.Failed : ProvisioningStatus.Succeeded,
            ProvisioningError = residual > 0 ? $"residual-permissions:{residual}" : null,
            ProvisionedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
