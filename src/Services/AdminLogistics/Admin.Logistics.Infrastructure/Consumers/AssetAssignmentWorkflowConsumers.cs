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
        if (!EventSchemaVersion.Supports(context.Message.SchemaVersion, supportedMax: 2, logger, nameof(IAssetAssignmentApproved))
            || !WorkflowOutcomeLink.IsCurrentVersion(context.Message.SchemaVersion, logger, nameof(IAssetAssignmentApproved)))
            return;

        var m = context.Message;
        var assignment = await db.AssetAssignments
            .Include(a => a.Asset)
            .FirstOrDefaultAsync(a => a.Id == m.AssignmentId, context.CancellationToken);
        if (assignment is null || assignment.Status != AssetAssignmentStatus.PendingApproval)
            return;

        if (!WorkflowOutcomeLink.IsLinked(m.WorkflowInstanceId, assignment.WorkflowInstanceId, assignment.Id, logger, nameof(IAssetAssignmentApproved)))
            return;

        // PLAN-43: the asset comes from the assignment, not from the message.
        var open = await db.AssetAssignments.AsNoTracking()
            .Where(a => a.AssetId == assignment.AssetId && a.ReturnedAt == null && a.Id != m.AssignmentId)
            .ToListAsync(context.CancellationToken);
        AssetApprovalRules.ApplyApproved(assignment, assignment.Asset, open);
        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Asset assignment {AssignmentId} ended {Status} after approval", m.AssignmentId, assignment.Status);
    }
}

public class AssetAssignmentRejectedConsumer(
    AdminLogisticsDbContext db,
    ILogger<AssetAssignmentRejectedConsumer> logger) : IConsumer<IAssetAssignmentRejected>
{
    public async Task Consume(ConsumeContext<IAssetAssignmentRejected> context)
    {
        if (!EventSchemaVersion.Supports(context.Message.SchemaVersion, supportedMax: 2, logger, nameof(IAssetAssignmentRejected))
            || !WorkflowOutcomeLink.IsCurrentVersion(context.Message.SchemaVersion, logger, nameof(IAssetAssignmentRejected)))
            return;

        var assignment = await db.AssetAssignments.FindAsync([context.Message.AssignmentId], context.CancellationToken);
        if (assignment is null || assignment.Status != AssetAssignmentStatus.PendingApproval)
            return;

        if (!WorkflowOutcomeLink.IsLinked(context.Message.WorkflowInstanceId, assignment.WorkflowInstanceId, assignment.Id, logger, nameof(IAssetAssignmentRejected)))
            return;

        AssetApprovalRules.ApplyRejected(assignment, context.Message.Reason, DateTime.UtcNow);
        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Asset assignment {AssignmentId} rejected", context.Message.AssignmentId);
    }
}
