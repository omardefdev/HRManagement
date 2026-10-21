using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Domain.Services;

public static class SupplyReceiptRules
{
    public static bool CanReceiveFromOrder(SupplyOrder order) =>
        order.Status == SupplyOrderStatus.Fulfilled;

    public static bool IsFullyReceived(SupplyOrder order)
    {
        if (order.Lines.Count == 0)
            return false;

        return order.Lines.All(l => l.QtyReceived >= l.QtyRequested);
    }

    public static ReceiptRecord BuildReceiptDraft(SupplyOrder order, Guid? receivedBy, DateTime receivedAtUtc)
    {
        var itemsSummary = order.Lines.Count > 0
            ? string.Join("؛ ", order.Lines.Select(l =>
                $"{l.Sku ?? l.InventoryItem?.Sku ?? "—"} × {l.QtyRequested}"))
            : order.Items;

        return new ReceiptRecord
        {
            SupplyOrderId = order.Id,
            ReceivedBy = receivedBy,
            ReceivedAt = receivedAtUtc,
            ItemsReceived = itemsSummary ?? $"استلام طلب {order.Id:N}"
        };
    }

    public static void ApplyReceivedQuantitiesToLines(SupplyOrder order, IReadOnlyDictionary<Guid, int> qtyByLineId)
    {
        foreach (var line in order.Lines)
        {
            if (qtyByLineId.TryGetValue(line.Id, out var qty) && qty > 0)
                line.QtyReceived = qty;
        }
    }

    public static IEnumerable<SupplyOrderLine> GetCapitalizableLines(SupplyOrder order) =>
        order.Lines.Where(l => l.IsCapitalizable && l.QtyReceived > 0);

    public static int RemainingAssetSlots(SupplyOrderLine line, int assetsCreatedFromLine) =>
        Math.Max(0, line.QtyReceived - assetsCreatedFromLine);
}
