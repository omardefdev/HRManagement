using Admin.Logistics.Domain.Entities;

namespace Admin.Logistics.Web.Services;

public sealed class InventoryItemIndexFilter : AdminLogisticsPagedFilter
{
    public string? Q { get; init; }
    public string? Location { get; init; }
    public string? Unit { get; init; }
    public int? QuantityMin { get; init; }
    public int? QuantityMax { get; init; }
    public bool LowStockOnly { get; init; }
    public bool OutOfStockOnly { get; init; }

    public IQueryable<InventoryItem> Apply(IQueryable<InventoryItem> query)
    {
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(x => x.Sku.Contains(term) || x.Name.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(Location))
            query = query.Where(x => x.Location == Location.Trim());

        if (!string.IsNullOrWhiteSpace(Unit))
            query = query.Where(x => x.Unit == Unit.Trim());

        if (QuantityMin.HasValue)
            query = query.Where(x => x.Quantity >= QuantityMin.Value);

        if (QuantityMax.HasValue)
            query = query.Where(x => x.Quantity <= QuantityMax.Value);

        if (LowStockOnly)
            query = query.Where(x => x.ReorderThreshold != null && x.Quantity <= x.ReorderThreshold);

        if (OutOfStockOnly)
            query = query.Where(x => x.Quantity == 0);

        return query;
    }
}
