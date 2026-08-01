using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Domain.Services;

namespace HRManagement.UnitTests;

public class ComplianceDomainRulesTests
{
    [Theory]
    [InlineData(AppealStatus.None, true)]
    [InlineData(AppealStatus.Pending, false)]
    public void DisciplinaryWorkflowRules_CanIssue(AppealStatus status, bool expected) =>
        Assert.Equal(expected, DisciplinaryWorkflowRules.CanIssue(status));

    [Theory]
    [InlineData(ActionType.Suspension, true)]
    [InlineData(ActionType.Warning, false)]
    public void DisciplinaryWorkflowRules_HasPayrollImpact(ActionType type, bool expected) =>
        Assert.Equal(expected, DisciplinaryWorkflowRules.HasPayrollImpact(type));

    [Fact]
    public void UnionEngagementGate_blocks_without_evidence_or_exemption()
    {
        Assert.False(UnionEngagementGate.CanStartWave(false, false, []));
        Assert.True(UnionEngagementGate.CanStartWave(true, false, []));
        Assert.True(UnionEngagementGate.CanStartWave(false, true, []));
    }

    [Fact]
    public void UnionEngagementGate_blocks_open_objections()
    {
        Assert.False(UnionEngagementGate.CanStartWave(true, false, ["Pending"]));
        Assert.False(UnionEngagementGate.CanStartWave(true, false, ["Escalated"]));
        Assert.True(UnionEngagementGate.CanStartWave(true, false, ["Resolved"]));
    }

    [Fact]
    public void UnionEngagementGate_rejects_collective_routed_to_individual()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            UnionEngagementGate.EnsureCollectiveNotRoutedToIndividual(true));
        Assert.Contains("TRN-002", ex.Message);
    }

    [Fact]
    public void InvestigationChecklistDefaults_creates_all_steps()
    {
        var complaintId = Guid.NewGuid();
        var items = InvestigationChecklistDefaults.CreateFor(complaintId).ToList();
        Assert.Equal(Enum.GetValues<InvestigationChecklistStep>().Length, items.Count);
        Assert.All(items, i => Assert.Equal(complaintId, i.ComplaintId));
    }

    [Fact]
    public void InvestigationChecklistDefaults_Mark_requires_evidence()
    {
        var item = new InvestigationChecklist { ComplaintId = Guid.NewGuid(), Step = InvestigationChecklistStep.Assigned };
        Assert.Throws<InvalidOperationException>(() => InvestigationChecklistDefaults.Mark(item, Guid.NewGuid(), "  "));
        InvestigationChecklistDefaults.Mark(item, Guid.NewGuid(), "ref-1");
        Assert.True(item.IsCompleted);
        Assert.Equal("ref-1", item.EvidenceRef);
    }

    [Fact]
    public void ComplaintProtectionRules_hides_anonymous_reporter_from_non_investigators()
    {
        var complaint = new Complaint { IsAnonymous = true, AssignedTo = Guid.NewGuid() };
        Assert.False(ComplaintProtectionRules.CanSeeReporter(complaint, Guid.NewGuid(), isInvestigatorRole: false));
        Assert.True(ComplaintProtectionRules.CanSeeReporter(complaint, complaint.AssignedTo, isInvestigatorRole: true));
    }

    [Fact]
    public void ComplaintProtectionRules_blocks_self_assignment()
    {
        var reporterId = Guid.NewGuid();
        var complaint = new Complaint { ReporterProtectedId = reporterId };
        Assert.False(ComplaintProtectionRules.CanAssignInvestigator(complaint, reporterId));
        Assert.True(ComplaintProtectionRules.CanAssignInvestigator(complaint, Guid.NewGuid()));
    }

    [Fact]
    public void ComplaintProtectionRules_SoftCancel_sets_flags()
    {
        var complaint = new Complaint();
        ComplaintProtectionRules.SoftCancel(complaint, "test");
        Assert.True(complaint.IsCancelled);
        Assert.NotNull(complaint.CancelledAt);
        Assert.Equal("test", complaint.CancellationReason);
    }
}
