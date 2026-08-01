using HR.Health.Domain.Entities;
using HR.Health.Domain.Enums;
using HR.Health.Domain.Services;
using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Domain.Services;
using HRManagement.MessageContracts.Events;

namespace HRManagement.UnitTests;

public class Phase4Tests
{
    // ── MedicalDecisionWorkflow ──

    [Fact]
    public void MedicalDecisionWorkflow_RequiresRetirement_TrueForRetirement()
    {
        Assert.True(MedicalDecisionWorkflow.RequiresRetirement(MedicalCommitteeRecommendation.Retirement));
        Assert.False(MedicalDecisionWorkflow.RequiresRetirement(MedicalCommitteeRecommendation.ReturnToWork));
    }

    [Fact]
    public void MedicalDecisionWorkflow_RequiresStatusUpdate_TrueForLimitedAndReturn()
    {
        Assert.True(MedicalDecisionWorkflow.RequiresStatusUpdate(MedicalCommitteeRecommendation.LimitedDuties));
        Assert.True(MedicalDecisionWorkflow.RequiresStatusUpdate(MedicalCommitteeRecommendation.ReturnToWork));
        Assert.False(MedicalDecisionWorkflow.RequiresStatusUpdate(MedicalCommitteeRecommendation.Retirement));
    }

    [Fact]
    public void MedicalDecisionWorkflow_ShouldPublishDecision_TrueForRetirementAndLimited()
    {
        Assert.True(MedicalDecisionWorkflow.ShouldPublishDecision(MedicalCommitteeRecommendation.Retirement));
        Assert.True(MedicalDecisionWorkflow.ShouldPublishDecision(MedicalCommitteeRecommendation.LimitedDuties));
        Assert.False(MedicalDecisionWorkflow.ShouldPublishDecision(MedicalCommitteeRecommendation.ReturnToWork));
    }

    [Fact]
    public void MedicalDecisionWorkflow_CreateRetirementRecord_SetsFields()
    {
        var empId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 1);
        var record = MedicalDecisionWorkflow.CreateRetirementRecord(empId, date, "سبب");
        Assert.Equal(empId, record.EmployeeId);
        Assert.Equal(date, record.EffectiveDate);
        Assert.Equal("سبب", record.Reason);
        Assert.NotEqual(Guid.Empty, record.Id);
    }

    [Fact]
    public void MedicalDecisionWorkflow_CreateStatusEntry_LimitedDuties()
    {
        var empId = Guid.NewGuid();
        var entry = MedicalDecisionWorkflow.CreateStatusEntry(empId, MedicalCommitteeRecommendation.LimitedDuties);
        Assert.Equal(empId, entry.EmployeeId);
        Assert.Equal("LIMITED_DUTIES", entry.StatusCode);
    }

    [Fact]
    public void MedicalDecisionWorkflow_CreateStatusEntry_ReturnToWork()
    {
        var empId = Guid.NewGuid();
        var entry = MedicalDecisionWorkflow.CreateStatusEntry(empId, MedicalCommitteeRecommendation.ReturnToWork);
        Assert.Equal("FIT_FOR_DUTY", entry.StatusCode);
    }

    [Fact]
    public void MedicalDecisionWorkflow_MapRecommendation_AllValues()
    {
        Assert.Equal("MEDICAL_RETIREMENT", MedicalDecisionWorkflow.MapRecommendationToMedicalRef(MedicalCommitteeRecommendation.Retirement));
        Assert.Equal("LIMITED_DUTIES", MedicalDecisionWorkflow.MapRecommendationToMedicalRef(MedicalCommitteeRecommendation.LimitedDuties));
        Assert.Equal("RETURN_TO_WORK", MedicalDecisionWorkflow.MapRecommendationToMedicalRef(MedicalCommitteeRecommendation.ReturnToWork));
        Assert.Equal("OTHER", MedicalDecisionWorkflow.MapRecommendationToMedicalRef(MedicalCommitteeRecommendation.Other));
    }

    // ── DisciplinaryWorkflowRules ──

    [Fact]
    public void DisciplinaryWorkflow_CanIssue_OnlyWhenNone()
    {
        Assert.True(DisciplinaryWorkflowRules.CanIssue(AppealStatus.None));
        Assert.False(DisciplinaryWorkflowRules.CanIssue(AppealStatus.Pending));
        Assert.False(DisciplinaryWorkflowRules.CanIssue(AppealStatus.Decided));
    }

    [Fact]
    public void DisciplinaryWorkflow_CanAppeal_OnlyWhenNone()
    {
        Assert.True(DisciplinaryWorkflowRules.CanAppeal(AppealStatus.None));
        Assert.False(DisciplinaryWorkflowRules.CanAppeal(AppealStatus.Pending));
    }

    [Fact]
    public void DisciplinaryWorkflow_CanDecide_OnlyWhenPending()
    {
        Assert.True(DisciplinaryWorkflowRules.CanDecide(AppealStatus.Pending));
        Assert.False(DisciplinaryWorkflowRules.CanDecide(AppealStatus.None));
        Assert.False(DisciplinaryWorkflowRules.CanDecide(AppealStatus.Decided));
    }

    [Fact]
    public void DisciplinaryWorkflow_HasPayrollImpact_SuspensionAndDismissal()
    {
        Assert.True(DisciplinaryWorkflowRules.HasPayrollImpact(ActionType.Suspension));
        Assert.True(DisciplinaryWorkflowRules.HasPayrollImpact(ActionType.Dismissal));
        Assert.False(DisciplinaryWorkflowRules.HasPayrollImpact(ActionType.Warning));
        Assert.False(DisciplinaryWorkflowRules.HasPayrollImpact(ActionType.Other));
    }

    [Fact]
    public void DisciplinaryWorkflow_IsSuspension()
    {
        Assert.True(DisciplinaryWorkflowRules.IsSuspension(ActionType.Suspension));
        Assert.False(DisciplinaryWorkflowRules.IsSuspension(ActionType.Warning));
    }

    // ── Complaint lifecycle ──

    [Fact]
    public void ComplaintStatus_HasAllValues()
    {
        var values = Enum.GetValues<ComplaintStatus>();
        Assert.Contains(ComplaintStatus.Open, values);
        Assert.Contains(ComplaintStatus.Investigating, values);
        Assert.Contains(ComplaintStatus.Resolved, values);
        Assert.Contains(ComplaintStatus.Closed, values);
    }

    [Fact]
    public void Complaint_FollowUps_Initialized()
    {
        var complaint = new Complaint { FiledBy = "أحمد", Subject = "شكوى" };
        Assert.NotNull(complaint.FollowUps);
        Assert.Empty(complaint.FollowUps);
    }

    [Fact]
    public void Complaint_CanAddFollowUp()
    {
        var complaint = new Complaint { Id = Guid.NewGuid(), FiledBy = "أحمد", Subject = "شكوى" };
        complaint.FollowUps.Add(new ComplaintFollowUp
        {
            Id = Guid.NewGuid(),
            ComplaintId = complaint.Id,
            ActionTaken = "تم التواصل"
        });
        Assert.Single(complaint.FollowUps);
    }

    // ── LegalCase lifecycle ──

    [Fact]
    public void CaseStatus_HasAllValues()
    {
        var values = Enum.GetValues<CaseStatus>();
        Assert.Contains(CaseStatus.Open, values);
        Assert.Contains(CaseStatus.Ongoing, values);
        Assert.Contains(CaseStatus.Closed, values);
    }

    [Fact]
    public void LegalCase_AssignedTo_Nullable()
    {
        var lc = new LegalCase { CaseNumber = "C001", Subject = "قضية" };
        Assert.Null(lc.AssignedTo);
        lc.AssignedTo = Guid.NewGuid();
        Assert.NotNull(lc.AssignedTo);
    }

    [Fact]
    public void LegalCase_DefaultStatus_Open()
    {
        var lc = new LegalCase { CaseNumber = "C002", Subject = "قضية" };
        Assert.Equal(CaseStatus.Open, lc.Status);
    }

    // ── InsuranceCard-InsuranceContract link ──

    [Fact]
    public void InsuranceCard_ContractFK_Nullable()
    {
        var card = new InsuranceCard { MemberNumber = "M001" };
        Assert.Null(card.InsuranceContractId);
        Assert.Null(card.InsuranceContract);
    }

    [Fact]
    public void InsuranceCard_CanLinkContract()
    {
        var contractId = Guid.NewGuid();
        var card = new InsuranceCard
        {
            MemberNumber = "M002",
            InsuranceContractId = contractId,
            InsuranceContract = new InsuranceContract { Id = contractId, Provider = "شركة" }
        };
        Assert.Equal(contractId, card.InsuranceContractId);
        Assert.Equal("شركة", card.InsuranceContract.Provider);
    }

    // ── DisciplinaryAction-Complaint link ──

    [Fact]
    public void DisciplinaryAction_ComplaintFK_Nullable()
    {
        var da = new DisciplinaryAction { EmployeeId = Guid.NewGuid() };
        Assert.Null(da.ComplaintId);
        Assert.Null(da.Complaint);
    }

    [Fact]
    public void DisciplinaryAction_CanLinkComplaint()
    {
        var complaintId = Guid.NewGuid();
        var da = new DisciplinaryAction
        {
            EmployeeId = Guid.NewGuid(),
            ComplaintId = complaintId,
            Complaint = new Complaint { Id = complaintId, FiledBy = "x", Subject = "s" }
        };
        Assert.Equal(complaintId, da.ComplaintId);
    }

    // ── Event SchemaVersions ──

    private record MedRecIssuedStub(Guid EmployeeId, string RecommendationType, string Details, DateTime IssuedAt, Guid RecommendationId) : IMedicalRecommendationIssued;
    private record DisciplinaryTakenStub(Guid EmployeeId, string ActionType, DateTime IssuedAt, string? Notes, Guid? ComplaintId, Guid? CorrelationId, string SourceSystem, Guid ActionId, int PenaltyDays = 0) : IDisciplinaryActionTaken;
    private record ComplaintResolvedStub(Guid ComplaintId, string Subject, string Resolution, DateTime ResolvedAt, Guid? CorrelationId, string SourceSystem) : IComplaintResolved;
    private record MedDecisionStub(Guid EmployeeId, DateTime EffectiveDate, string MedicalRef, string? Notes, string SourceSystem, string? CorrelationId) : IMedicalDecisionIssued;
    private record LegalJudgmentStub(Guid EmployeeId, DateTime EffectiveDate, string JudgmentRef, string? Notes, string SourceSystem, string? CorrelationId) : ILegalJudgmentIssued;

    [Fact]
    public void IMedicalRecommendationIssued_SchemaVersion2()
    {
        IMedicalRecommendationIssued msg = new MedRecIssuedStub(Guid.NewGuid(), "Retirement", "d", DateTime.UtcNow, Guid.NewGuid());
        Assert.Equal(2, msg.SchemaVersion);
        Assert.Null(msg.ReportId);
        Assert.Null(msg.CorrelationId);
        Assert.Null(msg.SourceSystem);
    }

    [Fact]
    public void IDisciplinaryActionTaken_SchemaVersion1()
    {
        IDisciplinaryActionTaken msg = new DisciplinaryTakenStub(Guid.NewGuid(), "Suspension", DateTime.UtcNow, null, null, null, "Compliance", Guid.NewGuid());
        Assert.Equal(1, msg.SchemaVersion);
    }

    [Fact]
    public void IComplaintResolved_SchemaVersion1()
    {
        IComplaintResolved msg = new ComplaintResolvedStub(Guid.NewGuid(), "موضوع", "حل", DateTime.UtcNow, null, "Compliance");
        Assert.Equal(1, msg.SchemaVersion);
    }

    [Fact]
    public void IMedicalDecisionIssued_SchemaVersion1()
    {
        IMedicalDecisionIssued msg = new MedDecisionStub(Guid.NewGuid(), DateTime.UtcNow, "RETIREMENT", null, "HR.Health", null);
        Assert.Equal(1, msg.SchemaVersion);
    }

    [Fact]
    public void ILegalJudgmentIssued_SchemaVersion1()
    {
        ILegalJudgmentIssued msg = new LegalJudgmentStub(Guid.NewGuid(), DateTime.UtcNow, "J001", null, "Compliance", null);
        Assert.Equal(1, msg.SchemaVersion);
    }

    // ── Entity properties ──

    [Fact]
    public void MedicalRecord_CommitteeReports_Initialized()
    {
        var mr = new MedicalRecord();
        Assert.NotNull(mr.CommitteeReports);
    }

    [Fact]
    public void MedicalCommitteeReport_Recommendations_Initialized()
    {
        var report = new MedicalCommitteeReport();
        Assert.NotNull(report.Recommendations);
    }

    [Fact]
    public void AuditFinding_Recommendations_Initialized()
    {
        var finding = new AuditFinding();
        Assert.NotNull(finding.Recommendations);
    }

    [Fact]
    public void MedicalRecommendationType_AllValues()
    {
        var values = Enum.GetValues<MedicalRecommendationType>();
        Assert.Contains(MedicalRecommendationType.FitnessForDuty, values);
        Assert.Contains(MedicalRecommendationType.LimitedDuties, values);
        Assert.Contains(MedicalRecommendationType.Retirement, values);
        Assert.Contains(MedicalRecommendationType.Compensation, values);
    }

    [Fact]
    public void FindingSeverity_AllValues()
    {
        var values = Enum.GetValues<FindingSeverity>();
        Assert.Equal(4, values.Length);
        Assert.Contains(FindingSeverity.Critical, values);
    }

    [Fact]
    public void HealthRetirementRecord_Properties()
    {
        var record = new HealthRetirementRecord
        {
            EmployeeId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2026, 1, 1),
            Reason = "طبي"
        };
        Assert.Equal("طبي", record.Reason);
    }

    [Fact]
    public void ComplianceRecommendation_FindingNavigation()
    {
        var findingId = Guid.NewGuid();
        var rec = new ComplianceRecommendation
        {
            FindingId = findingId,
            RecommendedAction = "إجراء",
            Finding = new AuditFinding { Id = findingId, Description = "desc" }
        };
        Assert.Equal(findingId, rec.FindingId);
        Assert.Equal("desc", rec.Finding.Description);
    }

    [Fact]
    public void LegalReferral_Properties()
    {
        var lr = new LegalReferral
        {
            ReferredTo = "المحكمة",
            ReferralDate = DateTime.UtcNow
        };
        Assert.Equal("المحكمة", lr.ReferredTo);
    }
}
