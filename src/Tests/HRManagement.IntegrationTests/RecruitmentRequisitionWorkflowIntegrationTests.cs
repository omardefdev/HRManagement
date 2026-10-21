using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class RecruitmentRequisitionWorkflowIntegrationTests
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
    public async Task WorkflowHandler_Approve_Sets_ApprovedDate()
    {
        await using var db = CreateContext();
        var org = new OrgUnit { Code = "OU-WF", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-WF",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-WF-001",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.PendingApproval,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(req);
        await db.SaveChangesAsync();

        var instance = new HR.Core.Domain.Entities.Workflow.WorkflowInstance
        {
            EntityType = "RecruitmentRequisition",
            EntityId = req.Id,
            Status = HRManagement.SharedKernel.Workflow.WorkflowStatus.Completed
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        var handler = new RecruitmentRequisitionWorkflowHandler(db);
        await handler.HandleAsync(instance, approved: true, reason: null);

        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentRequisitions.SingleAsync();
        Assert.Equal(RequisitionStatus.Approved, saved.Status);
        Assert.NotNull(saved.ApprovedDate);
    }

    [Fact]
    public async Task WorkflowHandler_Reject_Returns_To_Draft()
    {
        await using var db = CreateContext();
        var org = new OrgUnit { Code = "OU-WF2", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-WF2",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-WF-002",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.PendingApproval,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            WorkflowInstanceId = Guid.NewGuid()
        };
        db.RecruitmentRequisitions.Add(req);
        await db.SaveChangesAsync();

        var instance = new HR.Core.Domain.Entities.Workflow.WorkflowInstance
        {
            EntityType = "RecruitmentRequisition",
            EntityId = req.Id,
            Status = HRManagement.SharedKernel.Workflow.WorkflowStatus.Rejected
        };

        var handler = new RecruitmentRequisitionWorkflowHandler(db);
        await handler.HandleAsync(instance, approved: false, reason: "test");

        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentRequisitions.SingleAsync();
        Assert.Equal(RequisitionStatus.Draft, saved.Status);
        Assert.Null(saved.WorkflowInstanceId);
    }
}
