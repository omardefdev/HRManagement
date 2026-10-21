using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Web.Services;

public sealed class AssetIndexFilter : AdminLogisticsPagedFilter
{
    public string? Q { get; init; }
    public AssetStatus? Status { get; init; }
    public string? Category { get; init; }
    public string? Brand { get; init; }
    public Guid? HolderEmployeeId { get; init; }
    public bool UnassignedOnly { get; init; }
    public bool AssignedOnly { get; init; }
    public bool PrivilegedOnly { get; init; }
    public string? OriginKind { get; init; }
    public DateTime? PurchaseFrom { get; init; }
    public DateTime? PurchaseTo { get; init; }
    public decimal? ValueMin { get; init; }
    public decimal? ValueMax { get; init; }
    public bool ActiveOnly { get; init; }
    public bool ShowCancelled { get; init; }

    public IQueryable<Asset> Apply(IQueryable<Asset> query)
    {
        if (!ShowCancelled)
            query = query.Where(x => !x.IsCancelled);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(x =>
                x.TagNumber.Contains(term)
                || x.Name.Contains(term)
                || (x.Brand != null && x.Brand.Contains(term))
                || (x.Model != null && x.Model.Contains(term)));
        }

        if (Status.HasValue)
            query = query.Where(x => x.Status == Status.Value);
        else if (ActiveOnly)
            query = query.Where(x => !x.IsCancelled && x.Status != AssetStatus.Disposed);

        if (!string.IsNullOrWhiteSpace(Category))
            query = query.Where(x => x.Category == Category.Trim());

        if (!string.IsNullOrWhiteSpace(Brand))
            query = query.Where(x => x.Brand == Brand.Trim());

        if (HolderEmployeeId is Guid holderId && holderId != Guid.Empty)
        {
            query = query.Where(a => a.Assignments.Any(aa =>
                aa.EmployeeId == holderId
                && aa.ReturnedAt == null
                && aa.Status == AssetAssignmentStatus.Active));
        }

        if (UnassignedOnly)
        {
            query = query.Where(a => !a.Assignments.Any(aa =>
                aa.ReturnedAt == null && aa.Status == AssetAssignmentStatus.Active));
        }

        if (AssignedOnly)
        {
            query = query.Where(a => a.Assignments.Any(aa =>
                aa.ReturnedAt == null && aa.Status == AssetAssignmentStatus.Active));
        }

        if (PrivilegedOnly)
            query = query.Where(x => x.IsPrivileged);

        switch (OriginKind?.Trim().ToLowerInvariant())
        {
            case "receipt":
                query = query.Where(x => x.OriginReceiptRecordId != null && !x.IsLegacyEntry);
                break;
            case "legacy":
                query = query.Where(x => x.IsLegacyEntry);
                break;
            case "none":
                query = query.Where(x => !x.IsLegacyEntry && !x.OriginReceiptRecordId.HasValue);
                break;
        }

        if (PurchaseFrom.HasValue)
        {
            var from = PurchaseFrom.Value.Date;
            query = query.Where(x => x.PurchaseDate >= from);
        }

        if (PurchaseTo.HasValue)
        {
            var toExclusive = PurchaseTo.Value.Date.AddDays(1);
            query = query.Where(x => x.PurchaseDate < toExclusive);
        }

        if (ValueMin.HasValue)
            query = query.Where(x => x.Value >= ValueMin.Value);

        if (ValueMax.HasValue)
            query = query.Where(x => x.Value <= ValueMax.Value);

        return query;
    }
}
