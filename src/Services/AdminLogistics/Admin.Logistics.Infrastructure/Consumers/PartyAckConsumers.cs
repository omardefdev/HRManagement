using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Infrastructure.Consumers;

public class SeparationPartyAckConsumer(AdminLogisticsDbContext db)
    : SeparationPartyAckConsumerBase(ProcessParties.AdminLogistics)
{
    protected override async Task<string?> ExecuteAsync(ConsumeContext<IExecuteSeparationParty> context)
    {
        var employeeId = context.Message.EmployeeId;
        var open = await db.AssetAssignments
            .Where(a => a.EmployeeId == employeeId && a.ReturnedAt == null)
            .ToListAsync(context.CancellationToken);

        foreach (var a in open)
        {
            a.ReturnedAt = DateTime.UtcNow;
            var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == a.AssetId, context.CancellationToken);
            if (asset is not null && asset.Status is not AssetStatus.Lost and not AssetStatus.Disposed)
                asset.Status = AssetStatus.InStorage;
        }

        if (open.Count > 0)
            await db.SaveChangesAsync(context.CancellationToken);

        return open.Count > 0 ? $"admin-logistics:returned:{open.Count}" : "admin-logistics:ack-noop";
    }

    protected override async Task<string?> CompensateAsync(ConsumeContext<IExecuteSeparationParty> context)
    {
        var employeeId = context.Message.EmployeeId;
        var returned = await db.AssetAssignments
            .Where(a => a.EmployeeId == employeeId && a.ReturnedAt != null)
            .ToListAsync(context.CancellationToken);

        foreach (var assignment in returned)
        {
            assignment.ReturnedAt = null;
            var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == assignment.AssetId, context.CancellationToken);
            if (asset is not null && asset.Status == AssetStatus.InStorage)
                asset.Status = AssetStatus.InUse;
        }

        if (returned.Count > 0)
            await db.SaveChangesAsync(context.CancellationToken);

        return returned.Count > 0 ? $"admin-logistics:compensate-reopened:{returned.Count}" : "admin-logistics:compensate-noop";
    }
}

public class TransferPartyAckConsumer() : TransferPartyAckConsumerBase(ProcessParties.AdminLogistics);
