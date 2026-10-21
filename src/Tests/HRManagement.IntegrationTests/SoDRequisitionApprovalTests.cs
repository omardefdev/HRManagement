using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class SoDRequisitionApprovalTests
{
    [Fact]
    public async Task Approve_Blocks_Requisition_Creator()
    {
        var creatorId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        await using var db = CreateContext();
        var org = new OrgUnit { Code = "OU-SOD", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-SOD",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-SOD-001",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.PendingApproval,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            CreatedBy = creatorId
        };
        db.RecruitmentRequisitions.Add(req);

        var definition = new HR.Core.Domain.Entities.Workflow.WorkflowDefinition
        {
            Code = "RECRUITMENT-REQUISITION-APPROVAL",
            Name = "test",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "RecruitmentRequisition",
            StepsJson = """[{"Level":1,"Name":"HR","StepType":0,"RoleRequired":"hr-core"}]""",
            GuardrailRolesJson = "[]"
        };
        db.WorkflowDefinitions.Add(definition);

        var instance = new HR.Core.Domain.Entities.Workflow.WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = "RecruitmentRequisition",
            EntityId = req.Id,
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = creatorId,
            StartedAt = DateTime.UtcNow
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        var engine = new Mock<IWorkflowEngine>();
        engine.Setup(e => e.ApproveStep(It.IsAny<WorkflowInstanceState>(), It.IsAny<WorkflowDefinitionSnapshot>(), creatorId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, string>?>()))
            .Returns(WorkflowEngineResult.Ok(WorkflowStatus.Completed, 2));

        var service = new WorkflowInstanceActionService(
            db,
            engine.Object,
            Mock.Of<IWorkflowSignatureService>(),
            Mock.Of<IPublishEndpoint>(),
            new RecruitmentRequisitionWorkflowHandler(db),
            new OfferLetterWorkflowHandler(db),
            new SelectionCommitteeWorkflowHandler(db),
            new RecruitmentAppealWorkflowHandler(db),
            new EstablishmentChangeWorkflowHandler(db, null!));

        var result = await service.ApproveAsync(
            instance.Id,
            new WorkflowActionActor(creatorId, "creator", "hr-management-approver"),
            comments: null,
            targetStepLevel: 1);

        Assert.False(result.Success);
        Assert.Equal("لا يمكن للمنشئ اعتماد طلبه الخاص.", result.Error);
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
