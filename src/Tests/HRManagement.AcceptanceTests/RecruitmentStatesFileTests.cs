using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>PLAN-33 P2: <c>recruitment-states.json</c> names exactly the ids the seed patch creates (runs without the stack).</summary>
public class RecruitmentStatesFileTests
{
    [Fact]
    public void Json_ids_equal_the_seed_constants()
    {
        var s = RecruitmentStates.Load();

        Assert.Equal(AcceptanceSeedIds.AgencyACode, s.Agency.Code);
        Assert.Equal(AcceptanceSeedIds.AgencyAId, s.Agency.TenantId);
        Assert.Equal(AcceptanceSeedIds.Br1OrgUnitId, s.Agency.ScopeOrgUnitId);

        AssertRequisition(s.Requisitions.Draft, AcceptanceSeedIds.RecruitmentStatesDraftRequisitionId,
            AcceptanceSeedIds.RecruitmentStatesDraftRequisitionNumber, "Draft");
        AssertRequisition(s.Requisitions.PendingStep1, AcceptanceSeedIds.RecruitmentStatesPendingStep1RequisitionId,
            AcceptanceSeedIds.RecruitmentStatesPendingStep1RequisitionNumber, "PendingApproval");
        AssertRequisition(s.Requisitions.PendingStep2, AcceptanceSeedIds.RecruitmentStatesPendingStep2RequisitionId,
            AcceptanceSeedIds.RecruitmentStatesPendingStep2RequisitionNumber, "PendingApproval");
        AssertRequisition(s.Requisitions.Approved, AcceptanceSeedIds.RecruitmentStatesApprovedRequisitionId,
            AcceptanceSeedIds.RecruitmentStatesApprovedRequisitionNumber, "Approved");
        AssertRequisition(s.Requisitions.Published, AcceptanceSeedIds.RecruitmentStatesPublishedRequisitionId,
            AcceptanceSeedIds.RecruitmentStatesPublishedRequisitionNumber, "Published");
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPendingStep1WorkflowId, s.Requisitions.PendingStep1.WorkflowInstanceId);
        Assert.Equal(1, s.Requisitions.PendingStep1.CurrentStep);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPendingStep2WorkflowId, s.Requisitions.PendingStep2.WorkflowInstanceId);
        Assert.Equal(2, s.Requisitions.PendingStep2.CurrentStep);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPublishedPostingId, s.Requisitions.Published.PostingId);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesCriteriaId, s.Requisitions.Published.CriteriaId);

        Assert.Equal(
        [
            AcceptanceSeedIds.RecruitmentStatesSubmittedApplicationId, AcceptanceSeedIds.RecruitmentStatesShortlistedApplicationId,
            AcceptanceSeedIds.RecruitmentStatesAssessedApplicationId, AcceptanceSeedIds.RecruitmentStatesRanked1ApplicationId,
            AcceptanceSeedIds.RecruitmentStatesRanked2ApplicationId, AcceptanceSeedIds.RecruitmentStatesRanked3ApplicationId,
            AcceptanceSeedIds.RecruitmentStatesOfferPendingApplicationId, AcceptanceSeedIds.RecruitmentStatesOfferIssuedApplicationId,
            AcceptanceSeedIds.RecruitmentStatesAppealApplicationId
        ],
        [
            s.Applications.Submitted, s.Applications.Shortlisted, s.Applications.Assessed, s.Applications.Ranked1, s.Applications.Ranked2,
            s.Applications.Ranked3, s.Applications.OfferPending, s.Applications.OfferIssued, s.Applications.RejectedWithAppeal
        ]);

        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPendingOfferId, s.Offers.PendingApproval.Id);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPendingOfferNumber, s.Offers.PendingApproval.Number);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPendingOfferWorkflowId, s.Offers.PendingApproval.WorkflowInstanceId);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesOfferPendingApplicationId, s.Offers.PendingApproval.ApplicationId);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesIssuedOfferId, s.Offers.Issued.Id);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesIssuedOfferNumber, s.Offers.Issued.Number);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesOfferIssuedApplicationId, s.Offers.Issued.ApplicationId);

        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPendingAppealId, s.Appeal.Id);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesAppealApplicationId, s.Appeal.ApplicationId);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPendingAppealWorkflowId, s.Appeal.WorkflowInstanceId);

        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesApprovedRequisitionId, s.Committee.RequisitionId);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesCommitteeReadySessionId, s.Committee.ReadySessionId);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesCommitteeShortSessionId, s.Committee.ShortSessionId);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesLegacyApprovalRequestId, s.LegacyApprovalRequest.Id);

        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPoolSize, s.PositionPool.Size);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPoolPositionId(0), s.PositionPool.FirstId);
        Assert.Equal(AcceptanceSeedIds.RecruitmentStatesPoolPositionId(s.PositionPool.Size - 1), s.PositionPool.LastId);

        foreach (var persona in new[] { s.Personas.Creator, s.Personas.Step1Approver, s.Personas.Step2Approver, s.Personas.AppealReviewer, s.Personas.OtherReviewer })
            Assert.NotNull(AcceptanceHarness.RequirePersona(persona));
    }

    private static void AssertRequisition(RecruitmentStates.Requisition actual, Guid id, string number, string status)
    {
        Assert.Equal(id, actual.Id);
        Assert.Equal(number, actual.Number);
        Assert.Equal(status, actual.Status);
    }
}
