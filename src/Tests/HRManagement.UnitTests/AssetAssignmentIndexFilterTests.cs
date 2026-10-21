using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class AssetAssignmentIndexFilterTests
{
    private static async Task<(AdminLogisticsDbContext Db, Guid Emp1, Guid Emp2)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AdminLogisticsDbContext(options);

        var asset1 = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000001",
            Name = "حاسوب",
            Status = AssetStatus.InUse
        };
        var asset2 = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000002",
            Name = "طابعة",
            Status = AssetStatus.InStorage
        };
        db.Assets.AddRange(asset1, asset2);

        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        var today = new DateTime(2026, 8, 6);

        db.AssetAssignments.AddRange(
            new AssetAssignment
            {
                Id = Guid.NewGuid(),
                AssetId = asset1.Id,
                EmployeeId = emp1,
                AssignedAt = today.AddDays(-10),
                DueBackDate = today.AddDays(-1),
                Status = AssetAssignmentStatus.Active
            },
            new AssetAssignment
            {
                Id = Guid.NewGuid(),
                AssetId = asset2.Id,
                EmployeeId = emp2,
                AssignedAt = today.AddDays(-5),
                ReturnedAt = today.AddDays(-1),
                Status = AssetAssignmentStatus.Returned
            },
            new AssetAssignment
            {
                Id = Guid.NewGuid(),
                AssetId = asset1.Id,
                EmployeeId = emp2,
                AssignedAt = today.AddDays(-2),
                Status = AssetAssignmentStatus.PendingApproval
            });

        await db.SaveChangesAsync();
        return (db, emp1, emp2);
    }

    private static async Task<List<AssetAssignment>> QueryAsync(
        AdminLogisticsDbContext db,
        AssetAssignmentIndexFilter filter)
    {
        var query = filter.Apply(db.AssetAssignments.AsNoTracking().Include(a => a.Asset).AsQueryable());
        return await query.OrderBy(a => a.AssignedAt).ToListAsync();
    }

    [Fact]
    public async Task Filter_by_assetQ_matches_tag_or_name()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new AssetAssignmentIndexFilter { AssetQ = "000002" });
            Assert.Single(items);
            Assert.Equal("طابعة", items[0].Asset!.Name);
        }
    }

    [Fact]
    public async Task Filter_by_employeeId()
    {
        var (db, emp1, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new AssetAssignmentIndexFilter { EmployeeId = emp1 });
            Assert.Single(items);
            Assert.Equal(emp1, items[0].EmployeeId);
        }
    }

    [Fact]
    public async Task Filter_by_status_takes_priority_over_openOnly()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new AssetAssignmentIndexFilter
            {
                Status = AssetAssignmentStatus.Returned,
                OpenOnly = true
            });
            Assert.Single(items);
            Assert.Equal(AssetAssignmentStatus.Returned, items[0].Status);
            Assert.NotNull(items[0].ReturnedAt);
        }
    }

    [Fact]
    public async Task Filter_openOnly_excludes_returned_and_rejected()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new AssetAssignmentIndexFilter { OpenOnly = true });
            Assert.Equal(2, items.Count);
            Assert.All(items, a => Assert.Null(a.ReturnedAt));
            Assert.All(items, a => Assert.NotEqual(AssetAssignmentStatus.Rejected, a.Status));
        }
    }

    [Fact]
    public async Task Filter_overdueOnly_returns_open_past_due_date()
    {
        var (db, emp1, _) = await SeedAsync();
        var today = new DateTime(2026, 8, 6);
        await using (db)
        {
            var items = await QueryAsync(db, new AssetAssignmentIndexFilter
            {
                OverdueOnly = true,
                Today = today
            });
            Assert.Single(items);
            Assert.Equal(emp1, items[0].EmployeeId);
        }
    }

    [Fact]
    public async Task Filter_assigned_date_range_is_inclusive()
    {
        var (db, _, _) = await SeedAsync();
        var today = new DateTime(2026, 8, 6);
        await using (db)
        {
            var items = await QueryAsync(db, new AssetAssignmentIndexFilter
            {
                AssignedFrom = today.AddDays(-6),
                AssignedTo = today.AddDays(-2),
                Today = today
            });
            Assert.Equal(2, items.Count);
        }
    }
}
