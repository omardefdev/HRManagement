using Admin.Logistics.Domain.Entities;
using HRManagement.SharedKernel.ViewModels;

namespace Admin.Logistics.Web.ViewModels;

public sealed class InventoryItemIndexViewModel
{
    public PaginatedList<InventoryItem> Items { get; init; } = PaginatedList<InventoryItem>.Empty();
    public string? Q { get; init; }
    public string? Location { get; init; }
    public string? Unit { get; init; }
    public int? QuantityMin { get; init; }
    public int? QuantityMax { get; init; }
    public bool LowStockOnly { get; init; }
    public bool OutOfStockOnly { get; init; }
}
