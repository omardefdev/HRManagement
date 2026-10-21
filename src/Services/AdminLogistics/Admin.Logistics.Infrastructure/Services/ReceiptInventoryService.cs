using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Infrastructure.Services;

public static class ReceiptInventoryService
{
    public static async Task ApplyReceiptAsync(AdminLogisticsDbContext db, ReceiptRecord receipt, CancellationToken ct = default)
    {
        if (!receipt.InventoryItemId.HasValue || !receipt.QuantityReceived.HasValue || receipt.QuantityReceived.Value <= 0)
            return;

        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == receipt.InventoryItemId.Value, ct);
        if (item is null) return;

        item.Quantity += receipt.QuantityReceived.Value;
    }

    public static async Task ApplyReceiptLinesAsync(
        AdminLogisticsDbContext db,
        SupplyOrder order,
        IReadOnlyDictionary<Guid, int> qtyByLineId,
        CancellationToken ct = default)
    {
        foreach (var line in order.Lines)
        {
            if (!qtyByLineId.TryGetValue(line.Id, out var qty) || qty <= 0)
                continue;

            line.QtyReceived = qty;

            if (!line.InventoryItemId.HasValue)
                continue;

            var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == line.InventoryItemId.Value, ct);
            if (item is not null)
                item.Quantity += qty;
        }

        await Task.CompletedTask;
    }
}
