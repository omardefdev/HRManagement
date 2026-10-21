using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Admin.Logistics.Infrastructure.Consumers;

public class AssetAssignmentApprovedConsumer(
    AdminLogisticsDbContext db,
    ILogger<AssetAssignmentApprovedConsumer> logger) : IConsumer<IAssetAssignmentApproved>
{
    public async Task Consume(ConsumeContext<IAssetAssignmentApproved> context)
    {
        if (!EventSchemaVersion.Supports(context.Message.SchemaVersion, supportedMax: 1, logger, nameof(IAssetAssignmentApproved)))
            return;

        var m = context.Message;
        var assignment = await db.AssetAssignments
            .Include(a => a.Asset)
            .FirstOrDefaultAsync(a => a.Id == m.AssignmentId, context.CancellationToken);
        if (assignment is null || assignment.Status != AssetAssignmentStatus.PendingApproval)
            return;

        var open = await db.AssetAssignments.AsNoTracking()
            .Where(a => a.AssetId == m.AssetId && a.ReturnedAt == null && a.Id != m.AssignmentId)
            .ToListAsync(context.CancellationToken);
        if (!AssetAssignmentRules.CanAssign(assignment.Asset, open))
        {
            assignment.Status = AssetAssignmentStatus.Rejected;
            assignment.ReturnedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        assignment.Status = AssetAssignmentStatus.Active;
        assignment.Asset.Status = AssetStatus.InUse;
        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Asset assignment {AssignmentId} activated after approval", m.AssignmentId);
    }
}

public class AssetAssignmentRejectedConsumer(
    AdminLogisticsDbContext db,
    ILogger<AssetAssignmentRejectedConsumer> logger) : IConsumer<IAssetAssignmentRejected>
{
    public async Task Consume(ConsumeContext<IAssetAssignmentRejected> context)
    {
        if (!EventSchemaVersion.Supports(context.Message.SchemaVersion, supportedMax: 1, logger, nameof(IAssetAssignmentRejected)))
            return;

        var assignment = await db.AssetAssignments.FindAsync([context.Message.AssignmentId], context.CancellationToken);
        if (assignment is null || assignment.Status != AssetAssignmentStatus.PendingApproval)
            return;

        assignment.Status = AssetAssignmentStatus.Rejected;
        assignment.ReturnedAt = DateTime.UtcNow;
        assignment.EvidenceRef = string.IsNullOrWhiteSpace(context.Message.Reason)
            ? assignment.EvidenceRef
            : $"rejected: {context.Message.Reason}".Trim();
        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Asset assignment {AssignmentId} rejected", context.Message.AssignmentId);
    }
}
