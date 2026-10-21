using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class SupplyOrderIndexFilterTests
{
    private static async Task<(AdminLogisticsDbContext Db, Guid Req1, Guid Req2, Guid Ou1, Guid OrderWithSku)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AdminLogisticsDbContext(options);

        var req1 = Guid.NewGuid();
        var req2 = Guid.NewGuid();
        var ou1 = Guid.NewGuid();
        var ou2 = Guid.NewGuid();
        var baseDate = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        var pending = new SupplyOrder
        {
            Id = Guid.NewGuid(),
            Items = "أجهزة حاسوب للقسم",
            Status = SupplyOrderStatus.Pending,
            RequestedBy = req1,
            OrgUnitId = ou1,
            OrderedAt = baseDate
        };
        var fulfilled = new SupplyOrder
        {
            Id = Guid.NewGuid(),
            Items = "قرطاسية",
            Status = SupplyOrderStatus.Fulfilled,
            RequestedBy = req2,
            OrgUnitId = ou2,
            OrderedAt = baseDate.AddDays(5),
            FulfilledAt = baseDate.AddDays(6)
        };
        var closed = new SupplyOrder
        {
            Id = Guid.NewGuid(),
            Items = "أثاث مكتبي",
            Status = SupplyOrderStatus.Closed,
            RequestedBy = req1,
            OrgUnitId = ou1,
            OrderedAt = baseDate.AddDays(10),
            FulfilledAt = baseDate.AddDays(11)
        };
        var withSku = new SupplyOrder
        {
            Id = Guid.NewGuid(),
            Items = "طلب ببند",
            Status = SupplyOrderStatus.Approved,
            RequestedBy = req2,
            OrgUnitId = ou2,
            OrderedAt = baseDate.AddDays(3)
        };

        db.SupplyOrders.AddRange(pending, fulfilled, closed, withSku);
        await db.SaveChangesAsync();

        db.SupplyOrderLines.Add(new SupplyOrderLine
        {
            Id = Guid.NewGuid(),
            SupplyOrderId = withSku.Id,
            Sku = "LAP-001",
            QtyRequested = 5
        });
        await db.SaveChangesAsync();

        return (db, req1, req2, ou1, withSku.Id);
    }

    private static async Task<List<SupplyOrder>> QueryAsync(
        AdminLogisticsDbContext db,
        SupplyOrderIndexFilter filter)
    {
        var query = filter.Apply(db.SupplyOrders.AsNoTracking());
        return await query.OrderBy(o => o.OrderedAt).ToListAsync();
    }

    [Fact]
    public async Task Filter_by_q_matches_items_text()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new SupplyOrderIndexFilter { Q = "قرطاسية" });
            Assert.Single(items);
            Assert.Equal(SupplyOrderStatus.Fulfilled, items[0].Status);
        }
    }

    [Fact]
    public async Task Filter_by_q_matches_line_sku()
    {
        var (db, _, _, _, orderId) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new SupplyOrderIndexFilter { Q = "LAP-001" });
            Assert.Single(items);
            Assert.Equal(orderId, items[0].Id);
        }
    }

    [Fact]
    public async Task Filter_by_status()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new SupplyOrderIndexFilter { Status = SupplyOrderStatus.Pending });
            Assert.Single(items);
            Assert.Equal(SupplyOrderStatus.Pending, items[0].Status);
        }
    }

    [Fact]
    public async Task Filter_by_requestedBy_and_orgUnitId()
    {
        var (db, req1, _, ou1, _) = await SeedAsync();
        await using (db)
        {
            var byRequester = await QueryAsync(db, new SupplyOrderIndexFilter { RequestedBy = req1 });
            Assert.Equal(2, byRequester.Count);
            Assert.All(byRequester, o => Assert.Equal(req1, o.RequestedBy));

            var byOrg = await QueryAsync(db, new SupplyOrderIndexFilter { OrgUnitId = ou1 });
            Assert.Equal(2, byOrg.Count);
            Assert.All(byOrg, o => Assert.Equal(ou1, o.OrgUnitId));
        }
    }

    [Fact]
    public async Task Filter_ordered_date_range_is_inclusive()
    {
        var (db, _, _, _, _) = await SeedAsync();
        var from = new DateTime(2026, 8, 4);
        var to = new DateTime(2026, 8, 6);
        await using (db)
        {
            var items = await QueryAsync(db, new SupplyOrderIndexFilter { OrderedFrom = from, OrderedTo = to });
            Assert.Equal(2, items.Count);
        }
    }

    [Fact]
    public async Task Filter_activeOnly_excludes_closed_and_cancelled()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new SupplyOrderIndexFilter { ActiveOnly = true });
            Assert.Equal(3, items.Count);
            Assert.All(items, o => Assert.NotEqual(SupplyOrderStatus.Closed, o.Status));
            Assert.All(items, o => Assert.NotEqual(SupplyOrderStatus.Cancelled, o.Status));
        }
    }

    [Fact]
    public async Task Filter_status_takes_priority_over_activeOnly()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new SupplyOrderIndexFilter
            {
                Status = SupplyOrderStatus.Closed,
                ActiveOnly = true
            });
            Assert.Single(items);
            Assert.Equal(SupplyOrderStatus.Closed, items[0].Status);
        }
    }

    [Fact]
    public async Task Filter_awaitingReceiptOnly_returns_fulfilled()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new SupplyOrderIndexFilter { AwaitingReceiptOnly = true });
            Assert.Single(items);
            Assert.Equal(SupplyOrderStatus.Fulfilled, items[0].Status);
        }
    }

    [Fact]
    public async Task Filter_fulfilled_date_range()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new SupplyOrderIndexFilter
            {
                FulfilledFrom = new DateTime(2026, 8, 12),
                FulfilledTo = new DateTime(2026, 8, 12)
            });
            Assert.Single(items);
            Assert.Equal(SupplyOrderStatus.Closed, items[0].Status);
        }
    }
}
