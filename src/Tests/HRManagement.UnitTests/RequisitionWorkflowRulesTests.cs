using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

public class RequisitionWorkflowRulesTests
{
    [Theory]
    [InlineData(FundedPositionStatus.Vacant, true)]
    [InlineData(FundedPositionStatus.Occupied, false)]
    [InlineData(FundedPositionStatus.Frozen, false)]
    public void CanCreate_Only_Allows_Vacant(FundedPositionStatus status, bool expected)
    {
        var position = new FundedPosition { Status = status };
        Assert.Equal(expected, RequisitionWorkflowRules.CanCreate(position));
    }

    [Theory]
    [InlineData(RequisitionStatus.Draft, true)]
    [InlineData(RequisitionStatus.PendingApproval, false)]
    [InlineData(RequisitionStatus.Approved, false)]
    public void CanEdit_Only_Allows_Draft(RequisitionStatus status, bool expected) =>
        Assert.Equal(expected, RequisitionWorkflowRules.CanEdit(status));

    [Theory]
    [InlineData(RequisitionStatus.Draft, true)]
    [InlineData(RequisitionStatus.Published, false)]
    public void CanDelete_Only_Allows_Draft(RequisitionStatus status, bool expected) =>
        Assert.Equal(expected, RequisitionWorkflowRules.CanDelete(status));

    [Fact]
    public void ValidateVacancyCount_Requires_One()
    {
        Assert.True(RequisitionWorkflowRules.ValidateVacancyCount(1));
        Assert.False(RequisitionWorkflowRules.ValidateVacancyCount(2));
    }

    [Theory]
    [InlineData(RequisitionStatus.Draft, RequisitionStatus.PendingApproval, true)]
    [InlineData(RequisitionStatus.Draft, RequisitionStatus.Approved, false)]
    [InlineData(RequisitionStatus.Approved, RequisitionStatus.Published, true)]
    [InlineData(RequisitionStatus.Published, RequisitionStatus.Closed, true)]
    public void CanTransition_Matches_Workflow(
        RequisitionStatus from,
        RequisitionStatus to,
        bool expected) =>
        Assert.Equal(expected, RequisitionWorkflowRules.CanTransition(from, to));

    [Fact]
    public void ActiveStatuses_Includes_Draft_PendingApproval_Approved()
    {
        Assert.Contains(RequisitionStatus.Draft, RequisitionWorkflowRules.ActiveStatuses);
        Assert.Contains(RequisitionStatus.PendingApproval, RequisitionWorkflowRules.ActiveStatuses);
        Assert.Contains(RequisitionStatus.Approved, RequisitionWorkflowRules.ActiveStatuses);
        Assert.DoesNotContain(RequisitionStatus.Closed, RequisitionWorkflowRules.ActiveStatuses);
    }

    [Fact]
    public void CanSubmit_Requires_Draft_And_Vacant_Position()
    {
        var vacant = new FundedPosition { Status = FundedPositionStatus.Vacant };
        var occupied = new FundedPosition { Status = FundedPositionStatus.Occupied };
        Assert.True(RequisitionWorkflowRules.CanSubmit(RequisitionStatus.Draft, vacant));
        Assert.False(RequisitionWorkflowRules.CanSubmit(RequisitionStatus.Approved, vacant));
        Assert.False(RequisitionWorkflowRules.CanSubmit(RequisitionStatus.Draft, occupied));
    }

    [Fact]
    public void CanPublish_Requires_Approved_Vacant_No_Posting()
    {
        var vacant = new FundedPosition { Status = FundedPositionStatus.Vacant };
        Assert.True(RequisitionWorkflowRules.CanPublish(RequisitionStatus.Approved, vacant, hasPublishedPosting: false));
        Assert.False(RequisitionWorkflowRules.CanPublish(RequisitionStatus.Draft, vacant, hasPublishedPosting: false));
        Assert.False(RequisitionWorkflowRules.CanPublish(RequisitionStatus.Approved, vacant, hasPublishedPosting: true));
    }

    [Theory]
    [InlineData(RequisitionStatus.PendingApproval, RequisitionStatus.Approved, true)]
    [InlineData(RequisitionStatus.PendingApproval, RequisitionStatus.Draft, true)]
    [InlineData(RequisitionStatus.Published, RequisitionStatus.Closed, true)]
    public void CanTransition_Plan2_Lifecycle(
        RequisitionStatus from,
        RequisitionStatus to,
        bool expected) =>
        Assert.Equal(expected, RequisitionWorkflowRules.CanTransition(from, to));
}
