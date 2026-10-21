using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Web.Services;

public sealed class AssetAssignmentIndexFilter : AdminLogisticsPagedFilter
{
    public string? AssetQ { get; init; }
    public Guid? EmployeeId { get; init; }
    public AssetAssignmentStatus? Status { get; init; }
    public bool OpenOnly { get; init; }
    public bool OverdueOnly { get; init; }
    public DateTime? AssignedFrom { get; init; }
    public DateTime? AssignedTo { get; init; }
    public DateTime Today { get; init; } = DateTime.Today;

    public IQueryable<AssetAssignment> Apply(IQueryable<AssetAssignment> query)
    {
        if (!string.IsNullOrWhiteSpace(AssetQ))
        {
            var term = AssetQ.Trim();
            query = query.Where(a => a.Asset!.TagNumber.Contains(term) || a.Asset.Name.Contains(term));
        }

        if (EmployeeId is Guid employeeId && employeeId != Guid.Empty)
            query = query.Where(a => a.EmployeeId == employeeId);

        if (Status.HasValue)
            query = query.Where(a => a.Status == Status.Value);
        else if (OpenOnly)
            query = query.Where(a => a.ReturnedAt == null && a.Status != AssetAssignmentStatus.Rejected);

        if (OverdueOnly)
        {
            var today = Today.Date;
            query = query.Where(a => a.ReturnedAt == null && a.DueBackDate != null && a.DueBackDate < today);
        }

        if (AssignedFrom.HasValue)
        {
            var from = AssignedFrom.Value.Date;
            query = query.Where(a => a.AssignedAt >= from);
        }

        if (AssignedTo.HasValue)
        {
            var toExclusive = AssignedTo.Value.Date.AddDays(1);
            query = query.Where(a => a.AssignedAt < toExclusive);
        }

        return query;
    }
}
