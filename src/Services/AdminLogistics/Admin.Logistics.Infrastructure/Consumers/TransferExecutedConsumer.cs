using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Admin.Logistics.Infrastructure.Consumers;

// INT-002: inbox handled by InboxConsumeFilter
public class TransferExecutedConsumer : IConsumer<ITransferExecuted>
{
    private readonly AdminLogisticsDbContext _db;
    private readonly ILogger<TransferExecutedConsumer> _logger;

    public TransferExecutedConsumer(AdminLogisticsDbContext db, ILogger<TransferExecutedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ITransferExecuted> context)
    {
        var m = context.Message;
        if (!EventSchemaVersion.Supports(m.SchemaVersion, supportedMax: 2, _logger, nameof(ITransferExecuted)))
            return;

        _logger.LogInformation(
            "Admin.Logistics: TransferExecuted EmployeeId={EmployeeId}, From={From}, To={To}",
            m.EmployeeId, m.FromOrgUnitId, m.ToOrgUnitId);

        var openAssignments = await _db.AssetAssignments
            .Where(a => a.EmployeeId == m.EmployeeId && a.ReturnedAt == null)
            .ToListAsync(context.CancellationToken);

        var returnedAt = m.ExecutedAt;
        var assetIds = openAssignments.Select(a => a.AssetId).Distinct().ToList();
        foreach (var a in openAssignments)
        {
            a.ReturnedAt = returnedAt;
            a.ReturnNotes = $"[إعادة بسبب نقل] من {m.FromOrgUnitId} إلى {m.ToOrgUnitId} — {returnedAt:yyyy-MM-dd}";
        }

        if (assetIds.Count > 0)
        {
            var assets = await _db.Assets.Where(x => assetIds.Contains(x.Id)).ToListAsync(context.CancellationToken);
            foreach (var asset in assets)
            {
                if (asset.Status != AssetStatus.Lost && asset.Status != AssetStatus.Disposed)
                    asset.Status = AssetStatus.InStorage;
            }
        }

        if (openAssignments.Count > 0)
            await _db.SaveChangesAsync(context.CancellationToken);

        var snap = await _db.EmployeeOrgUnitSnapshots.FindAsync([m.EmployeeId], context.CancellationToken);
        if (snap is null)
        {
            _db.EmployeeOrgUnitSnapshots.Add(new HRManagement.SharedKernel.Entities.EmployeeOrgUnitSnapshot
            {
                EmployeeId = m.EmployeeId,
                TenantId = m.TenantId ?? Guid.Empty,
                PrimaryOrgUnitId = m.ToOrgUnitId
            });
        }
        else
        {
            snap.PrimaryOrgUnitId = m.ToOrgUnitId;
            if (m.TenantId is { } tenantId)
                snap.TenantId = tenantId;
        }

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
