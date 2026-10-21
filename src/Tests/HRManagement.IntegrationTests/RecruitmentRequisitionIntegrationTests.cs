using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class RecruitmentRequisitionIntegrationTests
{
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

    [Fact]
    public async Task Create_Persists_Requisition_With_FundedPosition_Fk()
    {
        await using var db = CreateContext();
        var orgUnit = new OrgUnit { Code = "OU-1", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(orgUnit);
        var position = new FundedPosition
        {
            PositionCode = "VAC-0001",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = orgUnit.Id
        };
        db.FundedPositions.Add(position);
        await db.SaveChangesAsync();

        var requisition = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-2026-00001",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Draft,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(requisition);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentRequisitions
            .Include(r => r.FundedPosition)
            .SingleAsync();
        Assert.Equal("REQ-2026-00001", saved.RequisitionNumber);
        Assert.Equal(position.Id, saved.FundedPositionId);
        Assert.NotNull(saved.FundedPosition);
    }

    [Fact]
    public async Task Unique_Index_Prevents_Duplicate_RequisitionNumber_Per_Tenant()
    {
        await using var db = CreateContext();
        var orgUnit = new OrgUnit { Code = "OU-2", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(orgUnit);
        var position1 = new FundedPosition
        {
            PositionCode = "VAC-0002",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = orgUnit.Id
        };
        var position2 = new FundedPosition
        {
            PositionCode = "VAC-0003",
            GradeCode = "G6",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = orgUnit.Id
        };
        db.FundedPositions.AddRange(position1, position2);
        await db.SaveChangesAsync();

        db.RecruitmentRequisitions.Add(new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-2026-DUP",
            FundedPositionId = position1.Id,
            JobTitle = "أ",
            Status = RequisitionStatus.Draft,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        });
        await db.SaveChangesAsync();

        db.RecruitmentRequisitions.Add(new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-2026-DUP",
            FundedPositionId = position2.Id,
            JobTitle = "ب",
            Status = RequisitionStatus.Draft,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Delete_FundedPosition_Is_Restricted_When_Requisition_Exists()
    {
        await using var db = CreateContext();
        var orgUnit = new OrgUnit { Code = "OU-3", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(orgUnit);
        var position = new FundedPosition
        {
            PositionCode = "VAC-0004",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = orgUnit.Id
        };
        db.FundedPositions.Add(position);
        await db.SaveChangesAsync();

        db.RecruitmentRequisitions.Add(new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-2026-00004",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Draft,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var positionToDelete = await db.FundedPositions.FindAsync(position.Id);
        Assert.NotNull(positionToDelete);
        db.FundedPositions.Remove(positionToDelete);
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
