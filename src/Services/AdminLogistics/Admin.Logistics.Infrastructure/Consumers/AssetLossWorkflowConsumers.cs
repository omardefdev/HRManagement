using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using Admin.Logistics.Infrastructure.Abstractions;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Admin.Logistics.Infrastructure.Consumers;

public class AssetLossApprovedConsumer(
    AdminLogisticsDbContext db,
    IEmployeeLookup employeeLookup,
    ILogger<AssetLossApprovedConsumer> logger) : IConsumer<IAssetLossApproved>
{
    public async Task Consume(ConsumeContext<IAssetLossApproved> context)
    {
        if (!EventSchemaVersion.Supports(context.Message.SchemaVersion, supportedMax: 1, logger, nameof(IAssetLossApproved)))
            return;

        var m = context.Message;
        var report = await db.AssetLossReports
            .Include(r => r.Asset)
            .FirstOrDefaultAsync(r => r.Id == m.LossReportId, context.CancellationToken);
        if (report is null || report.Status != AssetLossReportStatus.PendingApproval)
            return;

        var open = await db.AssetAssignments
            .Where(a => a.AssetId == m.AssetId && a.ReturnedAt == null)
            .ToListAsync(context.CancellationToken);

        string? holderName = null;
        var holderId = open.FirstOrDefault()?.EmployeeId;
        if (holderId is Guid hid && hid != Guid.Empty)
        {
            var names = await employeeLookup.GetEmployeeDisplayNamesAsync([hid], context.CancellationToken);
            names.TryGetValue(hid, out holderName);
        }

        AssetLostRules.ApplyLost(report.Asset, open, report, holderName);
        report.SettlementApprovedAt = m.OccurredAt;
        report.SettlementApprovedBy = m.SourceSystem;
        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Asset loss report {ReportId} confirmed after approval", m.LossReportId);
    }
}

public class AssetLossRejectedConsumer(
    AdminLogisticsDbContext db,
    ILogger<AssetLossRejectedConsumer> logger) : IConsumer<IAssetLossRejected>
{
    public async Task Consume(ConsumeContext<IAssetLossRejected> context)
    {
        if (!EventSchemaVersion.Supports(context.Message.SchemaVersion, supportedMax: 1, logger, nameof(IAssetLossRejected)))
            return;

        var report = await db.AssetLossReports.FindAsync([context.Message.LossReportId], context.CancellationToken);
        if (report is null || report.Status != AssetLossReportStatus.PendingApproval)
            return;

        report.Status = AssetLossReportStatus.Rejected;
        if (!string.IsNullOrWhiteSpace(context.Message.Reason))
            report.Notes = string.IsNullOrWhiteSpace(report.Notes)
                ? $"مرفوض: {context.Message.Reason.Trim()}"
                : $"{report.Notes} — مرفوض: {context.Message.Reason.Trim()}";

        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Asset loss report {ReportId} rejected", context.Message.LossReportId);
    }
}
