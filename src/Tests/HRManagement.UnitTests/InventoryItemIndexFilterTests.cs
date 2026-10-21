using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class InventoryItemIndexFilterTests
{
    private static async Task<(AdminLogisticsDbContext Db, Guid LowStockId, Guid OutOfStockId)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AdminLogisticsDbContext(options);

        var normal = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Sku = "PEN-001",
            Name = "أقلام حبر",
            Quantity = 100,
            Unit = "علبة",
            Location = "مستودع أ",
            ReorderThreshold = 20
        };
        var lowStock = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Sku = "PAP-002",
            Name = "ورق A4",
            Quantity = 5,
            Unit = "رزمة",
            Location = "مستودع أ",
            ReorderThreshold = 10
        };
        var outOfStock = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Sku = "TON-003",
            Name = "حبر طابعة",
            Quantity = 0,
            Unit = "قطعة",
            Location = "مستودع ب",
            ReorderThreshold = 5
        };
        var otherLocation = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Sku = "DESK-004",
            Name = "مكتب خشبي",
            Quantity = 15,
            Unit = "قطعة",
            Location = "مستودع ج",
            ReorderThreshold = 3
        };
        var highQty = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Sku = "CHAIR-005",
            Name = "كرسي مكتب",
            Quantity = 50,
            Unit = "قطعة",
            Location = "مستودع ب",
            ReorderThreshold = null
        };

        db.InventoryItems.AddRange(normal, lowStock, outOfStock, otherLocation, highQty);
        await db.SaveChangesAsync();

        return (db, lowStock.Id, outOfStock.Id);
    }

    private static async Task<List<InventoryItem>> QueryAsync(
        AdminLogisticsDbContext db,
        InventoryItemIndexFilter filter)
    {
        var query = filter.Apply(db.InventoryItems.AsNoTracking());
        return await query.OrderBy(x => x.Sku).ToListAsync();
    }

    [Fact]
    public async Task Filter_by_q_matches_sku()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new InventoryItemIndexFilter { Q = "PAP-002" });
            Assert.Single(items);
            Assert.Equal("PAP-002", items[0].Sku);
        }
    }

    [Fact]
    public async Task Filter_by_q_matches_name()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new InventoryItemIndexFilter { Q = "طابعة" });
            Assert.Single(items);
            Assert.Equal("TON-003", items[0].Sku);
        }
    }

    [Fact]
    public async Task Filter_by_location()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new InventoryItemIndexFilter { Location = "مستودع أ" });
            Assert.Equal(2, items.Count);
            Assert.All(items, x => Assert.Equal("مستودع أ", x.Location));
        }
    }

    [Fact]
    public async Task Filter_by_unit()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new InventoryItemIndexFilter { Unit = "قطعة" });
            Assert.Equal(3, items.Count);
            Assert.All(items, x => Assert.Equal("قطعة", x.Unit));
        }
    }

    [Fact]
    public async Task Filter_quantity_range()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new InventoryItemIndexFilter { QuantityMin = 10, QuantityMax = 50 });
            Assert.Equal(2, items.Count);
            Assert.All(items, x => Assert.InRange(x.Quantity, 10, 50));
        }
    }

    [Fact]
    public async Task Filter_lowStockOnly()
    {
        var (db, lowStockId, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new InventoryItemIndexFilter { LowStockOnly = true });
            Assert.Equal(2, items.Count);
            Assert.Contains(items, x => x.Id == lowStockId);
            Assert.All(items, x =>
            {
                Assert.NotNull(x.ReorderThreshold);
                Assert.True(x.Quantity <= x.ReorderThreshold);
            });
        }
    }

    [Fact]
    public async Task Filter_outOfStockOnly()
    {
        var (db, _, outOfStockId) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new InventoryItemIndexFilter { OutOfStockOnly = true });
            Assert.Single(items);
            Assert.Equal(outOfStockId, items[0].Id);
            Assert.Equal(0, items[0].Quantity);
        }
    }

    [Fact]
    public async Task Filter_combined_location_and_lowStock()
    {
        var (db, lowStockId, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new InventoryItemIndexFilter
            {
                Location = "مستودع أ",
                LowStockOnly = true
            });
            Assert.Single(items);
            Assert.Equal(lowStockId, items[0].Id);
        }
    }
}
