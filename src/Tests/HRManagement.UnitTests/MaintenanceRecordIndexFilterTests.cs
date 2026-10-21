using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class MaintenanceRecordIndexFilterTests
{
    private static async Task<(AdminLogisticsDbContext Db, Guid AssetAId, Guid AssetBId, Guid PreventiveAId)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AdminLogisticsDbContext(options);

        var assetA = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000101",
            Name = "حاسوب مكتبي",
            Status = AssetStatus.InUse
        };
        var assetB = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000202",
            Name = "طابعة",
            Status = AssetStatus.InUse
        };
        db.Assets.AddRange(assetA, assetB);

        var preventiveA = new MaintenanceRecord
        {
            Id = Guid.NewGuid(),
            AssetId = assetA.Id,
            Asset = assetA,
            MaintenanceType = MaintenanceType.Preventive,
            PerformedAt = new DateTime(2026, 1, 10),
            PerformedBy = "فني أحمد",
            Notes = "تنظيف دوري"
        };
        var correctiveA = new MaintenanceRecord
        {
            Id = Guid.NewGuid(),
            AssetId = assetA.Id,
            Asset = assetA,
            MaintenanceType = MaintenanceType.Corrective,
            PerformedAt = new DateTime(2026, 2, 15),
            PerformedBy = "ورشة دمشق",
            Cost = 150m
        };
        var preventiveB = new MaintenanceRecord
        {
            Id = Guid.NewGuid(),
            AssetId = assetB.Id,
            Asset = assetB,
            MaintenanceType = MaintenanceType.Preventive,
            PerformedAt = new DateTime(2026, 3, 1),
            PerformedBy = "فني خارجي"
        };
        var noDate = new MaintenanceRecord
        {
            Id = Guid.NewGuid(),
            AssetId = assetB.Id,
            Asset = assetB,
            MaintenanceType = MaintenanceType.Corrective,
            PerformedAt = null
        };

        db.MaintenanceRecords.AddRange(preventiveA, correctiveA, preventiveB, noDate);
        await db.SaveChangesAsync();

        return (db, assetA.Id, assetB.Id, preventiveA.Id);
    }

    private static async Task<List<MaintenanceRecord>> QueryAsync(
        AdminLogisticsDbContext db,
        MaintenanceRecordIndexFilter filter)
    {
        var query = filter.Apply(db.MaintenanceRecords.AsNoTracking().Include(m => m.Asset));
        return await query.OrderByDescending(m => m.PerformedAt).ToListAsync();
    }

    [Fact]
    public async Task Filter_by_q_matches_tag_number()
    {
        var (db, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new MaintenanceRecordIndexFilter { Q = "000202" });
            Assert.Equal(2, items.Count);
            Assert.All(items, m => Assert.Equal("AH-2026-000202", m.Asset!.TagNumber));
        }
    }

    [Fact]
    public async Task Filter_by_q_matches_performed_by()
    {
        var (db, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new MaintenanceRecordIndexFilter { Q = "فني أحمد" });
            Assert.Single(items);
            Assert.Equal("تنظيف دوري", items[0].Notes);
        }
    }

    [Fact]
    public async Task Filter_by_assetId()
    {
        var (db, assetAId, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new MaintenanceRecordIndexFilter { AssetId = assetAId });
            Assert.Equal(2, items.Count);
            Assert.All(items, m => Assert.Equal(assetAId, m.AssetId));
        }
    }

    [Fact]
    public async Task Filter_by_maintenanceType()
    {
        var (db, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new MaintenanceRecordIndexFilter
            {
                MaintenanceType = MaintenanceType.Preventive
            });
            Assert.Equal(2, items.Count);
            Assert.All(items, m => Assert.Equal(MaintenanceType.Preventive, m.MaintenanceType));
        }
    }

    [Fact]
    public async Task Filter_by_performedFrom()
    {
        var (db, _, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new MaintenanceRecordIndexFilter
            {
                PerformedFrom = new DateTime(2026, 2, 1)
            });
            Assert.Equal(2, items.Count);
            Assert.All(items, m => Assert.True(m.PerformedAt >= new DateTime(2026, 2, 1)));
        }
    }

    [Fact]
    public async Task Filter_by_performedTo()
    {
        var (db, _, _, preventiveAId) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new MaintenanceRecordIndexFilter
            {
                PerformedTo = new DateTime(2026, 1, 31)
            });
            Assert.Single(items);
            Assert.Equal(preventiveAId, items[0].Id);
        }
    }

    [Fact]
    public async Task Filter_combined_assetId_and_maintenanceType()
    {
        var (db, assetAId, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new MaintenanceRecordIndexFilter
            {
                AssetId = assetAId,
                MaintenanceType = MaintenanceType.Corrective
            });
            Assert.Single(items);
            Assert.Equal(MaintenanceType.Corrective, items[0].MaintenanceType);
            Assert.Equal(assetAId, items[0].AssetId);
        }
    }
}
