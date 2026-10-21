using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;

namespace HRManagement.UnitTests;

public class AssetOriginRulesTests
{
    [Fact]
    public void Valid_legacy_with_reason_and_no_receipt()
    {
        var asset = new Asset
        {
            TagNumber = "L1",
            Name = "Gift",
            IsLegacyEntry = true,
            LegacyEntryReason = "هبة من جهة مانحة"
        };
        Assert.True(AssetOriginRules.IsValidOrigin(asset));
        Assert.True(AssetOriginRules.RequiresOriginReceipt(asset) == false);
    }

    [Fact]
    public void Invalid_new_without_receipt()
    {
        var asset = new Asset { TagNumber = "N1", Name = "New", IsLegacyEntry = false };
        Assert.False(AssetOriginRules.IsValidOrigin(asset));
        Assert.True(AssetOriginRules.RequiresOriginReceipt(asset));
    }

    [Fact]
    public void Valid_new_with_receipt()
    {
        var asset = new Asset
        {
            TagNumber = "N2",
            Name = "New",
            OriginReceiptRecordId = Guid.NewGuid()
        };
        Assert.True(AssetOriginRules.IsValidOrigin(asset));
    }

    [Fact]
    public void Cannot_clear_origin_after_set()
    {
        var existing = new Asset
        {
            TagNumber = "X",
            Name = "X",
            OriginReceiptRecordId = Guid.NewGuid()
        };
        var updated = new Asset
        {
            TagNumber = "X",
            Name = "X",
            OriginReceiptRecordId = null
        };
        Assert.False(AssetOriginRules.CanChangeOrigin(existing, updated));
    }

    [Fact]
    public void Grandfathered_without_origin()
    {
        var asset = new Asset { TagNumber = "G1", Name = "Old stock" };
        Assert.True(AssetOriginRules.IsGrandfatheredWithoutOrigin(asset));
    }
}

public class SupplyReceiptRulesTests
{
    [Fact]
    public void Can_receive_only_from_fulfilled_order()
    {
        var pending = new SupplyOrder { Status = SupplyOrderStatus.Pending };
        var fulfilled = new SupplyOrder { Status = SupplyOrderStatus.Fulfilled };
        var closed = new SupplyOrder { Status = SupplyOrderStatus.Closed };
        Assert.False(SupplyReceiptRules.CanReceiveFromOrder(pending));
        Assert.True(SupplyReceiptRules.CanReceiveFromOrder(fulfilled));
        Assert.False(SupplyReceiptRules.CanReceiveFromOrder(closed));
    }

    [Fact]
    public void IsFullyReceived_when_all_lines_received()
    {
        var order = new SupplyOrder
        {
            Lines =
            [
                new SupplyOrderLine { QtyRequested = 5, QtyReceived = 5 },
                new SupplyOrderLine { QtyRequested = 3, QtyReceived = 1 }
            ]
        };
        Assert.False(SupplyReceiptRules.IsFullyReceived(order));
        order.Lines.Last().QtyReceived = 3;
        Assert.True(SupplyReceiptRules.IsFullyReceived(order));
    }

    [Fact]
    public void IsFullyReceived_false_without_lines() =>
        Assert.False(SupplyReceiptRules.IsFullyReceived(new SupplyOrder()));

    [Fact]
    public void BuildReceiptDraft_summarizes_lines()
    {
        var order = new SupplyOrder
        {
            Id = Guid.NewGuid(),
            Items = "fallback",
            Lines =
            [
                new SupplyOrderLine { Sku = "SKU-A", QtyRequested = 2, InventoryItem = new InventoryItem { Sku = "INV-A" } },
                new SupplyOrderLine { Sku = "SKU-B", QtyRequested = 5 }
            ]
        };
        var receipt = SupplyReceiptRules.BuildReceiptDraft(order, Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(order.Id, receipt.SupplyOrderId);
        Assert.Contains("SKU-A", receipt.ItemsReceived);
        Assert.Contains("SKU-B", receipt.ItemsReceived);
    }

    [Fact]
    public void ApplyReceivedQuantities_updates_lines()
    {
        var lineId = Guid.NewGuid();
        var order = new SupplyOrder
        {
            Lines = [new SupplyOrderLine { Id = lineId, QtyRequested = 10 }]
        };
        SupplyReceiptRules.ApplyReceivedQuantitiesToLines(order, new Dictionary<Guid, int> { [lineId] = 7 });
        Assert.Equal(7, order.Lines.First().QtyReceived);
    }

    [Fact]
    public void GetCapitalizableLines_filters_received()
    {
        var order = new SupplyOrder
        {
            Lines =
            [
                new SupplyOrderLine { IsCapitalizable = true, QtyReceived = 2 },
                new SupplyOrderLine { IsCapitalizable = true, QtyReceived = 0 },
                new SupplyOrderLine { IsCapitalizable = false, QtyReceived = 5 }
            ]
        };
        var lines = SupplyReceiptRules.GetCapitalizableLines(order).ToList();
        Assert.Single(lines);
        Assert.Equal(2, lines[0].QtyReceived);
    }

    [Fact]
    public void RemainingAssetSlots_subtracts_created()
    {
        var line = new SupplyOrderLine { QtyReceived = 5 };
        Assert.Equal(3, SupplyReceiptRules.RemainingAssetSlots(line, 2));
        Assert.Equal(0, SupplyReceiptRules.RemainingAssetSlots(line, 8));
    }
}

public class Fun005ReceiptLineageItTests
{
    [Fact]
    public void Fulfill_to_receipt_to_asset_lineage_fields()
    {
        var orderId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        var order = new SupplyOrder
        {
            Id = orderId,
            Status = SupplyOrderStatus.Fulfilled,
            Lines =
            [
                new SupplyOrderLine
                {
                    Id = lineId,
                    IsCapitalizable = true,
                    QtyRequested = 3,
                    UnitCost = 500m,
                    Sku = "LAP-001"
                }
            ]
        };

        Assert.True(SupplyReceiptRules.CanReceiveFromOrder(order));

        var qty = new Dictionary<Guid, int> { [lineId] = 3 };
        SupplyReceiptRules.ApplyReceivedQuantitiesToLines(order, qty);

        var receipt = SupplyReceiptRules.BuildReceiptDraft(order, Guid.NewGuid(), DateTime.UtcNow);
        receipt.Id = receiptId;

        var asset = new Asset
        {
            TagNumber = "AST-TEST",
            Name = "Laptop",
            Status = AssetStatus.InStorage,
            OriginReceiptRecordId = receiptId,
            OriginSupplyOrderLineId = lineId,
            Value = order.Lines.First().UnitCost,
            PurchaseDate = receipt.ReceivedAt
        };

        Assert.True(AssetOriginRules.IsValidOrigin(asset));
        Assert.Equal(2, SupplyReceiptRules.RemainingAssetSlots(order.Lines.First(), 1));
    }

    [Fact]
    public void E4_scenario_create_from_receipt_requires_origin()
    {
        var receiptId = Guid.NewGuid();
        var asset = new Asset
        {
            TagNumber = "E4",
            Name = "From receipt",
            OriginReceiptRecordId = receiptId
        };
        Assert.True(AssetOriginRules.IsValidOrigin(asset));
        Assert.Equal(receiptId, asset.OriginReceiptRecordId);
    }
}
