using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.OrgStructure;
using HRManagement.SharedKernel.Legal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

public class Est003EstablishmentTemporalTests
{
    [Fact]
    public async Task Freeze_append_version_asof_before_is_vacant_after_is_frozen()
    {
        await using var db = CreateContext();
        var org = new OrgUnit { Code = "OU-T", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "POS-T",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        await db.SaveChangesAsync();

        var versions = new FundedPositionVersionService(db);
        await versions.AppendFromPositionAsync(position, EstablishmentOperationType.PositionCreated, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var establishment = new EstablishmentService();
        establishment.FreezePosition(position, "موازنة");
        await db.SaveChangesAsync();
        await versions.AppendFromPositionAsync(position, EstablishmentOperationType.Freeze, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var before = await versions.ResolveAsOfAsync(position.Id, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var after = await versions.ResolveAsOfAsync(position.Id, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(before);
        Assert.Equal(FundedPositionStatus.Vacant, before!.Status);
        Assert.NotNull(after);
        Assert.Equal(FundedPositionStatus.Frozen, after!.Status);
    }

    [Fact]
    public async Task Occupied_append_version_asof_reflects_occupancy()
    {
        await using var db = CreateContext();
        var org = new OrgUnit { Code = "OU-O", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "POS-O",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        await db.SaveChangesAsync();

        var versions = new FundedPositionVersionService(db);
        await versions.AppendFromPositionAsync(position, EstablishmentOperationType.PositionCreated, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var employee = new Employee
        {
            NationalId = "12345678901",
            FirstName = "Test",
            LastName = "User",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var establishment = new EstablishmentService();
        establishment.AssignEmployee(position, employee.Id);
        await db.SaveChangesAsync();
        await versions.AppendFromPositionAsync(position, EstablishmentOperationType.Occupied, new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));

        var before = await versions.ResolveAsOfAsync(position.Id, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var after = await versions.ResolveAsOfAsync(position.Id, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(before);
        Assert.Equal(FundedPositionStatus.Vacant, before!.Status);
        Assert.Null(before.OccupantEmployeeId);
        Assert.NotNull(after);
        Assert.Equal(FundedPositionStatus.Occupied, after!.Status);
        Assert.Equal(employee.Id, after.OccupantEmployeeId);
    }

    [Fact]
    public async Task OrgUnit_asof_uses_position_versions_not_live_status()
    {
        await using var db = CreateContext();
        var org = new OrgUnit { Code = "OU-A", NameAr = "فرع", NameEn = "Branch" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "POS-A",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        await db.SaveChangesAsync();

        var fundedVersions = new FundedPositionVersionService(db);
        await fundedVersions.AppendFromPositionAsync(position, EstablishmentOperationType.PositionCreated, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        new EstablishmentService().FreezePosition(position, "test");
        await db.SaveChangesAsync();
        await fundedVersions.AppendFromPositionAsync(position, EstablishmentOperationType.Freeze, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var orgVersions = new OrgUnitVersionService(db, fundedVersions);
        await orgVersions.AppendVersionAsync(org, OrgOperationType.Create, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var asOf = await orgVersions.ResolveAsOfAsync(org.Id, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(asOf);
        Assert.Single(asOf!.FundedPositions);
        Assert.Equal(FundedPositionStatus.Vacant, asOf.FundedPositions[0].Status);
    }

    [Fact]
    public async Task Catalog_update_version_asof_returns_old_title()
    {
        await using var db = CreateContext();
        var entry = new JobCatalogEntry { JobCode = "JC-T", TitleAr = "قديم", GradeCode = "G4", IsActive = true };
        db.JobCatalogEntries.Add(entry);
        await db.SaveChangesAsync();

        var versions = new JobCatalogVersionService(db);
        await versions.AppendFromEntryAsync(entry, EstablishmentOperationType.Create, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        entry.TitleAr = "جديد";
        await db.SaveChangesAsync();
        await versions.AppendFromEntryAsync(entry, EstablishmentOperationType.Update, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var before = await versions.ResolveAsOfAsync(entry.Id, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(before);
        Assert.Equal("قديم", before!.TitleAr);
    }

    [Fact]
    public async Task AsOf_before_position_created_returns_null()
    {
        await using var db = CreateContext();
        var org = new OrgUnit { Code = "OU-N", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "POS-N",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id,
            CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.FundedPositions.Add(position);
        await db.SaveChangesAsync();

        var versions = new FundedPositionVersionService(db);
        await versions.AppendFromPositionAsync(position, EstablishmentOperationType.PositionCreated, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await versions.ResolveAsOfAsync(position.Id, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Null(result);
    }

    private static HRCoreDbContext CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new HRCoreDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
