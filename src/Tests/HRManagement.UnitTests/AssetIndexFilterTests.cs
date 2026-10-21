using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class AssetIndexFilterTests
{
    private static async Task<(AdminLogisticsDbContext Db, Guid HolderId, Guid ReceiptId, Guid AssignedAssetId, Guid UnassignedAssetId)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AdminLogisticsDbContext(options);

        var holderId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var purchaseDate = new DateTime(2026, 3, 15);

        var privileged = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000001",
            Name = "حاسوب مميز",
            Category = "IT",
            Brand = "Dell",
            Model = "Latitude",
            Status = AssetStatus.InStorage,
            IsPrivileged = true,
            Value = 5000m,
            PurchaseDate = purchaseDate,
            OriginReceiptRecordId = receiptId
        };
        var legacy = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000002",
            Name = "مكتب قديم",
            Category = "أثاث",
            Brand = "Generic",
            Status = AssetStatus.InStorage,
            IsLegacyEntry = true,
            LegacyEntryReason = "هبة قديمة",
            Value = 800m,
            PurchaseDate = purchaseDate.AddMonths(-6)
        };
        var noOrigin = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000003",
            Name = "طابعة بدون مصدر",
            Category = "IT",
            Brand = "HP",
            Status = AssetStatus.InStorage,
            Value = 1200m
        };
        var assigned = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000004",
            Name = "لابتوب موظف",
            Category = "IT",
            Brand = "Lenovo",
            Status = AssetStatus.InUse,
            Value = 3500m,
            PurchaseDate = purchaseDate.AddDays(10),
            OriginReceiptRecordId = receiptId
        };
        var disposed = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000005",
            Name = "شاشة مستبعدة",
            Category = "IT",
            Brand = "Samsung",
            Status = AssetStatus.Disposed,
            Value = 400m
        };
        var cancelled = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000006",
            Name = "ملغى",
            Category = "IT",
            Status = AssetStatus.InStorage,
            IsCancelled = true
        };

        db.Assets.AddRange(privileged, legacy, noOrigin, assigned, disposed, cancelled);
        await db.SaveChangesAsync();

        db.AssetAssignments.Add(new AssetAssignment
        {
            Id = Guid.NewGuid(),
            AssetId = assigned.Id,
            EmployeeId = holderId,
            AssignedAt = DateTime.UtcNow.AddDays(-5),
            Status = AssetAssignmentStatus.Active
        });
        await db.SaveChangesAsync();

        return (db, holderId, receiptId, assigned.Id, noOrigin.Id);
    }

    private static async Task<List<Asset>> QueryAsync(AdminLogisticsDbContext db, AssetIndexFilter filter)
    {
        var query = filter.Apply(db.Assets.AsNoTracking());
        return await query.OrderBy(a => a.TagNumber).ToListAsync();
    }

    [Fact]
    public async Task Filter_by_q_matches_name_and_brand()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var byName = await QueryAsync(db, new AssetIndexFilter { Q = "لابتوب" });
            Assert.Single(byName);
            Assert.Equal("AH-2026-000004", byName[0].TagNumber);

            var byBrand = await QueryAsync(db, new AssetIndexFilter { Q = "Dell" });
            Assert.Single(byBrand);
            Assert.Equal("AH-2026-000001", byBrand[0].TagNumber);
        }
    }

    [Fact]
    public async Task Filter_by_holderEmployeeId()
    {
        var (db, holderId, _, assignedId, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new AssetIndexFilter { HolderEmployeeId = holderId });
            Assert.Single(items);
            Assert.Equal(assignedId, items[0].Id);
        }
    }

    [Fact]
    public async Task Filter_unassignedOnly_and_assignedOnly()
    {
        var (db, _, _, assignedId, unassignedId) = await SeedAsync();
        await using (db)
        {
            var assigned = await QueryAsync(db, new AssetIndexFilter { AssignedOnly = true });
            Assert.Single(assigned);
            Assert.Equal(assignedId, assigned[0].Id);

            var unassigned = await QueryAsync(db, new AssetIndexFilter { UnassignedOnly = true });
            Assert.Contains(unassigned, a => a.Id == unassignedId);
            Assert.DoesNotContain(unassigned, a => a.Id == assignedId);
        }
    }

    [Fact]
    public async Task Filter_originKind()
    {
        var (db, _, receiptId, _, _) = await SeedAsync();
        await using (db)
        {
            var receipt = await QueryAsync(db, new AssetIndexFilter { OriginKind = "receipt" });
            Assert.Equal(2, receipt.Count);
            Assert.All(receipt, a => Assert.Equal(receiptId, a.OriginReceiptRecordId));

            var legacy = await QueryAsync(db, new AssetIndexFilter { OriginKind = "legacy" });
            Assert.Single(legacy);
            Assert.True(legacy[0].IsLegacyEntry);

            var none = await QueryAsync(db, new AssetIndexFilter { OriginKind = "none" });
            Assert.Equal(2, none.Count);
            Assert.Contains(none, a => a.TagNumber == "AH-2026-000003");
        }
    }

    [Fact]
    public async Task Filter_privilegedOnly()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new AssetIndexFilter { PrivilegedOnly = true });
            Assert.Single(items);
            Assert.True(items[0].IsPrivileged);
        }
    }

    [Fact]
    public async Task Filter_activeOnly_excludes_disposed_and_cancelled()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new AssetIndexFilter { ActiveOnly = true });
            Assert.Equal(4, items.Count);
            Assert.All(items, a =>
            {
                Assert.False(a.IsCancelled);
                Assert.NotEqual(AssetStatus.Disposed, a.Status);
            });
        }
    }

    [Fact]
    public async Task Filter_purchase_date_range()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new AssetIndexFilter
            {
                PurchaseFrom = new DateTime(2026, 3, 1),
                PurchaseTo = new DateTime(2026, 3, 31)
            });
            Assert.Equal(2, items.Count);
        }
    }

    [Fact]
    public async Task Filter_value_range()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new AssetIndexFilter { ValueMin = 1000m, ValueMax = 4000m });
            Assert.Equal(2, items.Count);
            Assert.All(items, a => Assert.InRange(a.Value!.Value, 1000m, 4000m));
        }
    }

    [Fact]
    public async Task Filter_combined_category_and_brand()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new AssetIndexFilter { Category = "IT", Brand = "HP" });
            Assert.Single(items);
            Assert.Equal("AH-2026-000003", items[0].TagNumber);
        }
    }

    [Fact]
    public async Task Filter_showCancelled_includes_cancelled()
    {
        var (db, _, _, _, _) = await SeedAsync();
        await using (db)
        {
            var hidden = await QueryAsync(db, new AssetIndexFilter { ShowCancelled = false });
            Assert.DoesNotContain(hidden, a => a.IsCancelled);

            var shown = await QueryAsync(db, new AssetIndexFilter { ShowCancelled = true });
            Assert.Contains(shown, a => a.IsCancelled);
        }
    }
}
