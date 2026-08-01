using HRManagement.SharedKernel.Interfaces;
using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Enums;
using IT.Infra.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IT.Infra.Infrastructure.Consumers;

public class EmployeeCreatedConsumer : IConsumer<IEmployeeCreated>
{
    private readonly ITInfraDbContext _db;
    private readonly IDirectoryEntrySyncService _directorySync;
    private readonly ILogger<EmployeeCreatedConsumer> _logger;

    public EmployeeCreatedConsumer(
        ITInfraDbContext db,
        IDirectoryEntrySyncService directorySync,
        ILogger<EmployeeCreatedConsumer> logger)
    {
        _db = db;
        _directorySync = directorySync;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IEmployeeCreated> context)
    {
        var m = context.Message;
        var marker = ConsumerMessageMarkers.EmployeeCreated(m.EmployeeId);

        var existing = await _db.ITSupportTickets.FirstOrDefaultAsync(t =>
                t.Description != null && t.Description.Contains(marker), context.CancellationToken);
        if (existing is not null)
            return;

        _logger.LogInformation(
            "IT.Infra: EmployeeCreated EmployeeId={EmployeeId}, creating provisioning ticket",
            m.EmployeeId);

        var ticket = new ITSupportTicket
        {
            Id = Guid.NewGuid(),
            IssueSummary = $"تجهيز حساب وأجهزة للموظف {m.FullName}",
            // The employee id identifies the subject for provisioning. Putting the national
            // identifier in a free-text ticket description exposed it to every IT support agent
            // and to anything that exports tickets.
            Description = $"{marker} موظف جديد: {m.FullName} ({m.EmployeeId:N}) — {m.EmploymentType}",
            Status = TicketStatus.Open,
            ReportedBy = m.EmployeeId,
            ProvisioningStatus = ProvisioningStatus.Pending
        };
        _db.ITSupportTickets.Add(ticket);
        await _db.SaveChangesAsync(context.CancellationToken);

        var ok = await _directorySync.SyncEmployeeCreatedAsync(
            m.EmployeeId,
            m.FullName,
            m.NationalIdHash,
            m.PrimaryOrgUnitId,
            context.CancellationToken);

        ticket.ProvisionedAt = DateTime.UtcNow;
        if (ok)
        {
            ticket.ProvisioningStatus = ProvisioningStatus.Succeeded;
            ticket.ProvisioningError = null;
        }
        else
        {
            ticket.ProvisioningStatus = ProvisioningStatus.Failed;
            ticket.ProvisioningError = "Directory sync failed";
            _logger.LogWarning("IT.Infra: provisioning Failed for EmployeeId={EmployeeId}", m.EmployeeId);
        }

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
