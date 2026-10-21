namespace Admin.Logistics.Web.ViewModels;

using Admin.Logistics.Domain.Entities;
using HRManagement.SharedKernel.ViewModels;

public sealed class CreateReceiptFromOrderViewModel
{
    public Guid SupplyOrderId { get; init; }
    public string? OrderSummary { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public Guid? ReceivedBy { get; set; }
    public List<ReceiptLineInputViewModel> Lines { get; set; } = [];
}

public sealed class ReceiptLineInputViewModel
{
    public Guid LineId { get; init; }
    public string? Sku { get; init; }
    public int QtyRequested { get; init; }
    public int QtyReceived { get; set; }
    public bool IsCapitalizable { get; init; }
    public Guid? InventoryItemId { get; init; }
}

public sealed class ReceiptCapitalizableLineViewModel
{
    public Guid LineId { get; init; }
    public string Label { get; init; } = default!;
    public int QtyReceived { get; init; }
    public int AssetsCreated { get; init; }
    public int Remaining => Math.Max(0, QtyReceived - AssetsCreated);
    public decimal? UnitCost { get; init; }
}

public sealed class ReceiptDetailsViewModel
{
    public required ReceiptRecord Receipt { get; init; }
    public int LinkedAssetCount { get; init; }
    public int ExpectedCapitalizableCount { get; init; }
    public IReadOnlyList<ReceiptCapitalizableLineViewModel> CapitalizableLines { get; init; } = [];
    public IReadOnlyList<Asset> LinkedAssets { get; init; } = [];
    public string? ReceivedByName { get; init; }
}

public sealed class ReceiptRecordIndexViewModel
{
    public PaginatedList<ReceiptRecord> Items { get; init; } = PaginatedList<ReceiptRecord>.Empty();
    public IReadOnlyDictionary<Guid, int> AssetCounts { get; init; } = new Dictionary<Guid, int>();
    public string? Q { get; init; }
    public Guid? SupplyOrderId { get; init; }
    public string? FilterSupplyOrderSummary { get; init; }
    public Guid? ReceivedBy { get; init; }
    public string? FilterReceivedByName { get; init; }
    public DateTime? ReceivedFrom { get; init; }
    public DateTime? ReceivedTo { get; init; }
}
