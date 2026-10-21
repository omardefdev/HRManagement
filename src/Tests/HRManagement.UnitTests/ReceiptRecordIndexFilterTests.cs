using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class ReceiptRecordIndexFilterTests
{
    private static readonly Guid EmployeeA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EmployeeB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static async Task<(AdminLogisticsDbContext Db, Guid OrderAId, Guid OrderBId, Guid ReceiptEarlyId)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AdminLogisticsDbContext(options);

        var inventory = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-2026-000001",
            Name = "حبر طابعة",
            Quantity = 10
        };
        db.InventoryItems.Add(inventory);

        var orderA = new SupplyOrder
        {
            Id = Guid.NewGuid(),
            Items = "طلب أجهزة مكتبية"
        };
        var orderB = new SupplyOrder
        {
            Id = Guid.NewGuid(),
            Items = "طلب قرطاسية"
        };
        db.SupplyOrders.AddRange(orderA, orderB);

        var receiptEarly = new ReceiptRecord
        {
            Id = Guid.NewGuid(),
            SupplyOrderId = orderA.Id,
            SupplyOrder = orderA,
            ReceivedBy = EmployeeA,
            ReceivedAt = new DateTime(2026, 1, 5),
            ItemsReceived = "حاسوب + شاشة"
        };
        var receiptMid = new ReceiptRecord
        {
            Id = Guid.NewGuid(),
            SupplyOrderId = orderA.Id,
            SupplyOrder = orderA,
            ReceivedBy = EmployeeB,
            ReceivedAt = new DateTime(2026, 2, 10),
            ItemsReceived = "لوحة مفاتيح",
            InventoryItemId = inventory.Id,
            InventoryItem = inventory,
            QuantityReceived = 2
        };
        var receiptOtherOrder = new ReceiptRecord
        {
            Id = Guid.NewGuid(),
            SupplyOrderId = orderB.Id,
            SupplyOrder = orderB,
            ReceivedBy = EmployeeA,
            ReceivedAt = new DateTime(2026, 3, 1),
            ItemsReceived = "أقلام ودفاتر"
        };
        var receiptNoDate = new ReceiptRecord
        {
            Id = Guid.NewGuid(),
            SupplyOrderId = orderB.Id,
            SupplyOrder = orderB,
            ReceivedBy = EmployeeB,
            ReceivedAt = null,
            ItemsReceived = "مسودة"
        };

        db.ReceiptRecords.AddRange(receiptEarly, receiptMid, receiptOtherOrder, receiptNoDate);
        await db.SaveChangesAsync();

        return (db, orderA.Id, orderB.Id, receiptEarly.Id);
    }

    private static async Task<List<ReceiptRecord>> QueryAsync(
        AdminLogisticsDbContext db,
        ReceiptRecordIndexFilter filter)
    {
        var query = filter.Apply(
            db.ReceiptRecords.AsNoTracking()
                .Include(r => r.SupplyOrder)
                .Include(r => r.InventoryItem));
        return await query.OrderByDescending(r => r.ReceivedAt).ToListAsync();
    }

    [Fact]
    public async Task Filter_by_q_matches_items_received()
    {
        var (db, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new ReceiptRecordIndexFilter { Q = "حاسوب" });
            Assert.Single(items);
            Assert.Contains("حاسوب", items[0].ItemsReceived);
        }
    }

    [Fact]
    public async Task Filter_by_q_matches_inventory_sku()
    {
        var (db, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new ReceiptRecordIndexFilter { Q = "SKU-2026" });
            Assert.Single(items);
            Assert.Equal("SKU-2026-000001", items[0].InventoryItem!.Sku);
        }
    }

    [Fact]
    public async Task Filter_by_supplyOrderId()
    {
        var (db, orderAId, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new ReceiptRecordIndexFilter { SupplyOrderId = orderAId });
            Assert.Equal(2, items.Count);
            Assert.All(items, r => Assert.Equal(orderAId, r.SupplyOrderId));
        }
    }

    [Fact]
    public async Task Filter_by_receivedBy()
    {
        var (db, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new ReceiptRecordIndexFilter { ReceivedBy = EmployeeA });
            Assert.Equal(2, items.Count);
            Assert.All(items, r => Assert.Equal(EmployeeA, r.ReceivedBy));
        }
    }

    [Fact]
    public async Task Filter_by_receivedFrom()
    {
        var (db, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new ReceiptRecordIndexFilter
            {
                ReceivedFrom = new DateTime(2026, 2, 1)
            });
            Assert.Equal(2, items.Count);
            Assert.All(items, r => Assert.True(r.ReceivedAt >= new DateTime(2026, 2, 1)));
        }
    }

    [Fact]
    public async Task Filter_by_receivedTo()
    {
        var (db, _, _, receiptEarlyId) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new ReceiptRecordIndexFilter
            {
                ReceivedTo = new DateTime(2026, 1, 31)
            });
            Assert.Single(items);
            Assert.Equal(receiptEarlyId, items[0].Id);
        }
    }

    [Fact]
    public async Task Filter_combined_supplyOrderId_and_receivedFrom()
    {
        var (db, orderAId, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new ReceiptRecordIndexFilter
            {
                SupplyOrderId = orderAId,
                ReceivedFrom = new DateTime(2026, 2, 1)
            });
            Assert.Single(items);
            Assert.Equal("لوحة مفاتيح", items[0].ItemsReceived);
        }
    }
}
