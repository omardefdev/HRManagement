using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Web.Services;

public sealed class MaintenanceRecordIndexFilter : AdminLogisticsPagedFilter
{
    public string? Q { get; init; }
    public Guid? AssetId { get; init; }
    public MaintenanceType? MaintenanceType { get; init; }
    public DateTime? PerformedFrom { get; init; }
    public DateTime? PerformedTo { get; init; }

    public IQueryable<MaintenanceRecord> Apply(IQueryable<MaintenanceRecord> query)
    {
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(m =>
                m.Asset!.TagNumber.Contains(term)
                || m.Asset.Name.Contains(term)
                || (m.PerformedBy != null && m.PerformedBy.Contains(term))
                || (m.Notes != null && m.Notes.Contains(term)));
        }

        if (AssetId is Guid assetId && assetId != Guid.Empty)
            query = query.Where(m => m.AssetId == assetId);

        if (MaintenanceType.HasValue)
            query = query.Where(m => m.MaintenanceType == MaintenanceType.Value);

        if (PerformedFrom.HasValue)
        {
            var from = PerformedFrom.Value.Date;
            query = query.Where(m => m.PerformedAt != null && m.PerformedAt >= from);
        }

        if (PerformedTo.HasValue)
        {
            var toExclusive = PerformedTo.Value.Date.AddDays(1);
            query = query.Where(m => m.PerformedAt != null && m.PerformedAt < toExclusive);
        }

        return query;
    }
}
