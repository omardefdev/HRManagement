using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

public class RecruitmentWorkflowRulesTests
{
    [Fact]
    public void CanHire_Only_From_Shortlisted()
    {
        Assert.True(RecruitmentWorkflowRules.CanHire(RecruitmentApplicationStatus.Shortlisted));
        Assert.False(RecruitmentWorkflowRules.CanHire(RecruitmentApplicationStatus.Submitted));
    }

    [Fact]
    public void CanTransition_Blocks_Hired_To_Submitted()
    {
        Assert.False(RecruitmentWorkflowRules.CanTransition(
            RecruitmentApplicationStatus.Hired,
            RecruitmentApplicationStatus.Submitted));
    }

    [Fact]
    public void CanTransition_Allows_Submitted_To_UnderReview()
    {
        Assert.True(RecruitmentWorkflowRules.CanTransition(
            RecruitmentApplicationStatus.Submitted,
            RecruitmentApplicationStatus.UnderReview));
    }
}
