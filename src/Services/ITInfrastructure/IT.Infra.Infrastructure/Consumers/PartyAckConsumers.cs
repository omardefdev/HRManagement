using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using IT.Infra.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace IT.Infra.Infrastructure.Consumers;

public class SeparationPartyAckConsumer(ITInfraDbContext db)
    : SeparationPartyAckConsumerBase(ProcessParties.ItInfra)
{
    protected override async Task<string?> ExecuteAsync(ConsumeContext<IExecuteSeparationParty> context)
    {
        var employeeId = context.Message.EmployeeId;

        var openPermissions = await db.SystemPermissionRecords
            .Where(p => p.UserId == employeeId && p.RevokedAt == null)
            .ToListAsync(context.CancellationToken);
        foreach (var p in openPermissions)
            p.RevokedAt = DateTime.UtcNow;

        var devices = await db.UserDevices
            .Where(d => d.AssignedTo == employeeId)
            .ToListAsync(context.CancellationToken);
        foreach (var d in devices)
            d.AssignedTo = null;

        if (openPermissions.Count > 0 || devices.Count > 0)
            await db.SaveChangesAsync(context.CancellationToken);

        return $"it-infra:detach:perm={openPermissions.Count}:dev={devices.Count}";
    }

    protected override async Task<string?> CompensateAsync(ConsumeContext<IExecuteSeparationParty> context)
    {
        var employeeId = context.Message.EmployeeId;
        var revokedPermissions = await db.SystemPermissionRecords
            .Where(p => p.UserId == employeeId && p.RevokedAt != null)
            .ToListAsync(context.CancellationToken);
        foreach (var p in revokedPermissions)
            p.RevokedAt = null;

        if (revokedPermissions.Count > 0)
            await db.SaveChangesAsync(context.CancellationToken);

        return revokedPermissions.Count > 0
            ? $"it-infra:compensate-restored-perm={revokedPermissions.Count}"
            : "it-infra:compensate-noop";
    }
}

public class HirePartyAckConsumer() : HirePartyAckConsumerBase(ProcessParties.ItInfra);
