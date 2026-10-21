using Admin.Logistics.Domain.Entities;

namespace Admin.Logistics.Web.Services;

public sealed class ReceiptRecordIndexFilter : AdminLogisticsPagedFilter
{
    public string? Q { get; init; }
    public Guid? SupplyOrderId { get; init; }
    public Guid? ReceivedBy { get; init; }
    public DateTime? ReceivedFrom { get; init; }
    public DateTime? ReceivedTo { get; init; }

    public IQueryable<ReceiptRecord> Apply(IQueryable<ReceiptRecord> query)
    {
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(r =>
                (r.ItemsReceived != null && r.ItemsReceived.Contains(term))
                || (r.SupplyOrder != null && r.SupplyOrder.Items != null && r.SupplyOrder.Items.Contains(term))
                || (r.InventoryItem != null && r.InventoryItem.Sku.Contains(term))
                || (r.InventoryItem != null && r.InventoryItem.Name.Contains(term)));
        }

        if (SupplyOrderId is Guid supplyOrderId && supplyOrderId != Guid.Empty)
            query = query.Where(r => r.SupplyOrderId == supplyOrderId);

        if (ReceivedBy is Guid receivedBy && receivedBy != Guid.Empty)
            query = query.Where(r => r.ReceivedBy == receivedBy);

        if (ReceivedFrom.HasValue)
        {
            var from = ReceivedFrom.Value.Date;
            query = query.Where(r => r.ReceivedAt != null && r.ReceivedAt >= from);
        }

        if (ReceivedTo.HasValue)
        {
            var toExclusive = ReceivedTo.Value.Date.AddDays(1);
            query = query.Where(r => r.ReceivedAt != null && r.ReceivedAt < toExclusive);
        }

        return query;
    }
}
