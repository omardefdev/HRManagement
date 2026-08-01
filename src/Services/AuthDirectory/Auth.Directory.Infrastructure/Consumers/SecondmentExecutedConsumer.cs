using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Directory.Infrastructure.Consumers;

public class SecondmentExecutedConsumer : IConsumer<ISecondmentExecuted>
{
    private readonly AuthDirectoryDbContext _db;
    private readonly ILogger<SecondmentExecutedConsumer> _logger;

    public SecondmentExecutedConsumer(AuthDirectoryDbContext db, ILogger<SecondmentExecutedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ISecondmentExecuted> context)
    {
        var m = context.Message;
        var marker = $"Secondment-{m.SecondmentRequestId}";

        if (await _db.AuthAuditLogs.AnyAsync(a =>
                a.Details != null && a.Details.Contains(marker)))
        {
            return;
        }

        _logger.LogInformation(
            "Auth.Directory: SecondmentExecuted EmployeeId={EmployeeId}, HostOrg={HostOrg}",
            m.EmployeeId, m.HostOrg);

        var entry = await _db.DirectoryEntries.FirstOrDefaultAsync(e => e.EmployeeId == m.EmployeeId);
        if (entry is not null)
        {
            entry.Status = $"Secondment@{m.HostOrg}";
            entry.LastSyncedAt = DateTime.UtcNow;
        }

        _db.AuthAuditLogs.Add(new AuthAuditLog
        {
            Action = "SecondmentExecuted",
            EntityType = nameof(DirectoryEntry),
            EntityId = entry?.Id ?? m.EmployeeId,
            Details = $"انتداب لدى {m.HostOrg} ({m.StartDate:d} - {m.EndDate:d}) — {marker}"
        });

        await _db.SaveChangesAsync();
    }
}
