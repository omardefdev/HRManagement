using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Web.Services;

public sealed class SupplyOrderIndexFilter : AdminLogisticsPagedFilter
{
    public string? Q { get; init; }
    public SupplyOrderStatus? Status { get; init; }
    public Guid? RequestedBy { get; init; }
    public Guid? OrgUnitId { get; init; }
    public DateTime? OrderedFrom { get; init; }
    public DateTime? OrderedTo { get; init; }
    public DateTime? FulfilledFrom { get; init; }
    public DateTime? FulfilledTo { get; init; }
    public bool ActiveOnly { get; init; }
    public bool AwaitingReceiptOnly { get; init; }

    public IQueryable<SupplyOrder> Apply(IQueryable<SupplyOrder> query)
    {
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(o =>
                (o.Items != null && o.Items.Contains(term))
                || o.Lines.Any(l => l.Sku != null && l.Sku.Contains(term)));
        }

        if (RequestedBy is Guid requesterId && requesterId != Guid.Empty)
            query = query.Where(o => o.RequestedBy == requesterId);

        if (OrgUnitId is Guid orgUnitId && orgUnitId != Guid.Empty)
            query = query.Where(o => o.OrgUnitId == orgUnitId);

        if (Status.HasValue)
            query = query.Where(o => o.Status == Status.Value);
        else if (ActiveOnly)
            query = query.Where(o => o.Status != SupplyOrderStatus.Closed && o.Status != SupplyOrderStatus.Cancelled);

        if (AwaitingReceiptOnly)
            query = query.Where(o => o.Status == SupplyOrderStatus.Fulfilled);

        if (OrderedFrom.HasValue)
        {
            var from = OrderedFrom.Value.Date;
            query = query.Where(o => o.OrderedAt >= from);
        }

        if (OrderedTo.HasValue)
        {
            var toExclusive = OrderedTo.Value.Date.AddDays(1);
            query = query.Where(o => o.OrderedAt < toExclusive);
        }

        if (FulfilledFrom.HasValue)
        {
            var from = FulfilledFrom.Value.Date;
            query = query.Where(o => o.FulfilledAt >= from);
        }

        if (FulfilledTo.HasValue)
        {
            var toExclusive = FulfilledTo.Value.Date.AddDays(1);
            query = query.Where(o => o.FulfilledAt < toExclusive);
        }

        return query;
    }
}
