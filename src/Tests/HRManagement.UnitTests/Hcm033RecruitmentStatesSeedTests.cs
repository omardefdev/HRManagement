using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-033 (PLAN-33, P2): <see cref="AcceptanceRecruitmentStatesPatch"/> seeds the deterministic recruitment states of
/// <c>testdata/acceptance/recruitment-states.json</c> once, with live workflow instances on the pending items and the
/// creator recorded as central-a's Keycloak id (so creator SoD and the work inbox behave as in a live run).
/// </summary>
public sealed class Hcm033RecruitmentStatesSeedTests
{
    [Fact]
    public void Keycloak_user_id_matches_the_realm_import_script()
    {
        // Values printed by Get-AcceptanceKeycloakUserId in scripts/prepare-keycloak-import.ps1.
        Assert.Equal(Guid.Parse("775c2919-e97a-1446-ab7e-eeea07401beb"), AcceptanceKeycloakIds.UserId("acc-central-a"));
        Assert.Equal(Guid.Parse("f8074858-ca9a-f843-b006-2609de1492ec"), AcceptanceKeycloakIds.UserId("acc-br1-mgr"));
        Assert.Equal(Guid.Parse("3050520d-0648-324b-988f-875c4d6ccca9"), AcceptanceKeycloakIds.PersonaUserId("a-management-approver"));
        Assert.Equal(Guid.Parse("4f3f6b85-f085-224a-a16d-bbe4d615674a"), AcceptanceKeycloakIds.PersonaUserId("a-compliance"));
    }

    [Fact]
    public async Task Recruitment_states_patch_seeds_the_ac_hcm_33_states_once()
    {
        // SQLite (relational): detects insert-order cycles the InMemory provider ignores (the patch must save in two phases).
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(connection).Options;
        await using (var seed = new HRCoreDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await AcceptanceDataSeeder.SeedAsync(seed, NullLogger.Instance);
            await WorkflowPackSeeder.SeedAsync(seed);
            await AcceptanceRecruitmentStatesPatch.ApplyAsync(seed, NullLogger.Instance);
            await AcceptanceRecruitmentStatesPatch.ApplyAsync(seed, NullLogger.Instance);
        }

        await using var db = new HRCoreDbContext(options);
        var centralA = AcceptanceKeycloakIds.PersonaUserId("central-a");

        var requisitions = await db.RecruitmentRequisitions.IgnoreQueryFilters()
            .Where(r => r.RequisitionNumber.StartsWith("REQ-2026-A-AC33-")).ToDictionaryAsync(r => r.Id);
        Assert.Equal(5, requisitions.Count);
        Assert.Equal(RequisitionStatus.Draft, requisitions[AcceptanceSeedIds.RecruitmentStatesDraftRequisitionId].Status);
        Assert.Equal(RequisitionStatus.PendingApproval, requisitions[AcceptanceSeedIds.RecruitmentStatesPendingStep1RequisitionId].Status);
        Assert.Equal(RequisitionStatus.PendingApproval, requisitions[AcceptanceSeedIds.RecruitmentStatesPendingStep2RequisitionId].Status);
        Assert.Equal(RequisitionStatus.Approved, requisitions[AcceptanceSeedIds.RecruitmentStatesApprovedRequisitionId].Status);
        var published = requisitions[AcceptanceSeedIds.RecruitmentStatesPublishedRequisitionId];
        Assert.Equal(RequisitionStatus.Published, published.Status);
        Assert.True(published.ApplicationClosingDate > DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.All(requisitions.Values, r =>
        {
            Assert.Equal(centralA, r.CreatedBy);
            Assert.Equal(AcceptanceSeedIds.AgencyAId, r.TenantId);
        });
        Assert.True(await db.RecruitmentAssessmentCriteria.IgnoreQueryFilters()
            .AnyAsync(c => c.Id == AcceptanceSeedIds.RecruitmentStatesCriteriaId && c.IsActive && c.RequisitionId == published.Id));

        var step1 = await Instance(db, AcceptanceSeedIds.RecruitmentStatesPendingStep1WorkflowId);
        Assert.Equal((AcceptanceSeedIds.RecruitmentStatesPendingStep1RequisitionId, 1, WorkflowStatus.InProgress, centralA),
            (step1.EntityId, step1.CurrentStepLevel, step1.Status, step1.InitiatedBy!.Value));
        Assert.Equal(step1.Id, requisitions[AcceptanceSeedIds.RecruitmentStatesPendingStep1RequisitionId].WorkflowInstanceId);
        var step2 = await Instance(db, AcceptanceSeedIds.RecruitmentStatesPendingStep2WorkflowId);
        Assert.Equal(2, step2.CurrentStepLevel);
        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(step2.StepHistoryJson)!;
        Assert.Equal(AcceptanceKeycloakIds.PersonaUserId("br1-mgr"), Assert.Single(history).ActorId);

        var apps = await db.RecruitmentApplications.IgnoreQueryFilters()
            .Where(a => a.RequisitionId == published.Id).ToDictionaryAsync(a => a.Id);
        Assert.Equal(9, apps.Count);
        Assert.Equal(RecruitmentApplicationStatus.Submitted, apps[AcceptanceSeedIds.RecruitmentStatesSubmittedApplicationId].Status);
        Assert.Equal(RecruitmentApplicationStatus.Shortlisted, apps[AcceptanceSeedIds.RecruitmentStatesShortlistedApplicationId].Status);
        Assert.Equal(RecruitmentApplicationStatus.Assessed, apps[AcceptanceSeedIds.RecruitmentStatesAssessedApplicationId].Status);
        Assert.Equal([1, 2, 3], new[]
        {
            AcceptanceSeedIds.RecruitmentStatesRanked1ApplicationId, AcceptanceSeedIds.RecruitmentStatesRanked2ApplicationId,
            AcceptanceSeedIds.RecruitmentStatesRanked3ApplicationId
        }.Select(id => Assert.IsType<int>(apps[id].Rank)));
        var assessors = await db.RecruitmentAssessmentScores.IgnoreQueryFilters()
            .Where(s => s.RecruitmentApplicationId == AcceptanceSeedIds.RecruitmentStatesAssessedApplicationId)
            .Select(s => s.AssessorUserId).Distinct().CountAsync();
        Assert.Equal(2, assessors);

        var pendingOffer = await db.OfferLetters.IgnoreQueryFilters().SingleAsync(o => o.Id == AcceptanceSeedIds.RecruitmentStatesPendingOfferId);
        Assert.Equal((OfferStatus.PendingApproval, centralA), (pendingOffer.Status, pendingOffer.CreatedBy!.Value));
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPendingOfferWorkflowId, pendingOffer.WorkflowInstanceId);
        Assert.Equal(nameof(HR.Core.Domain.Entities.OfferLetter), (await Instance(db, AcceptanceSeedIds.RecruitmentStatesPendingOfferWorkflowId)).EntityType);
        var issued = await db.OfferLetters.IgnoreQueryFilters().SingleAsync(o => o.Id == AcceptanceSeedIds.RecruitmentStatesIssuedOfferId);
        Assert.Equal(OfferStatus.Issued, issued.Status);
        Assert.True(issued.ExpiryDate > DateTime.UtcNow);

        var appeal = await db.RecruitmentAppeals.IgnoreQueryFilters().SingleAsync(a => a.Id == AcceptanceSeedIds.RecruitmentStatesPendingAppealId);
        Assert.Equal(RecruitmentAppealStatus.Pending, appeal.Status);
        Assert.Equal(AcceptancePersonaCatalog.Get("a-compliance").Employee!.Id, appeal.IndependentReviewerEmployeeId);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPendingAppealWorkflowId, appeal.WorkflowInstanceId);
        Assert.Equal(RecruitmentAppealStatus.Pending, apps[AcceptanceSeedIds.RecruitmentStatesAppealApplicationId].AppealStatus);

        var members = await db.SelectionCommitteeMembers.IgnoreQueryFilters()
            .Where(m => m.SessionId == AcceptanceSeedIds.RecruitmentStatesCommitteeReadySessionId).ToListAsync();
        Assert.Equal(4, members.Count);
        Assert.Single(members, m => m.Role == HR.Core.Domain.Entities.SelectionCommitteeMember.ChairRole);
        var personaEmployees = AcceptancePersonaCatalog.Personas.Where(p => p.Employee is not null).Select(p => p.Employee!.Id).ToHashSet();
        Assert.DoesNotContain(members, m => personaEmployees.Contains(m.EmployeeId));
        Assert.Equal(1, await db.SelectionCommitteeMembers.IgnoreQueryFilters()
            .CountAsync(m => m.SessionId == AcceptanceSeedIds.RecruitmentStatesCommitteeShortSessionId));

        var legacy = await db.ApprovalRequests.IgnoreQueryFilters().Include(r => r.Steps)
            .SingleAsync(r => r.Id == AcceptanceSeedIds.RecruitmentStatesLegacyApprovalRequestId);
        Assert.Equal((ApprovalStatus.Pending, centralA, "hr-core"), (legacy.Status, legacy.RequestedBy!.Value, Assert.Single(legacy.Steps).RoleRequired));

        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPoolSize, await db.FundedPositions.IgnoreQueryFilters()
            .CountAsync(p => p.PositionCode.StartsWith("AC33-POOL-") && p.Status == FundedPositionStatus.Vacant
                && p.OrgUnitId == AcceptanceSeedIds.Br1OrgUnitId));
    }

    private static Task<HR.Core.Domain.Entities.Workflow.WorkflowInstance> Instance(HRCoreDbContext db, Guid id) =>
        db.WorkflowInstances.IgnoreQueryFilters().SingleAsync(i => i.Id == id);
}
