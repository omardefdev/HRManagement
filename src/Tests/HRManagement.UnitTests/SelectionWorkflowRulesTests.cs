using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

public class SelectionWorkflowRulesTests
{
    [Fact]
    public void Shortlisted_Requires_Coi()
    {
        var app = new RecruitmentApplication { Status = RecruitmentApplicationStatus.UnderReview };
        Assert.False(SelectionWorkflowRules.CanTransition(app, RecruitmentApplicationStatus.Shortlisted));
        app.ConflictOfInterestDeclared = true;
        Assert.True(SelectionWorkflowRules.CanTransition(app, RecruitmentApplicationStatus.Shortlisted));
    }

    [Fact]
    public void Assessed_Requires_Score_And_Criteria()
    {
        var app = new RecruitmentApplication
        {
            Status = RecruitmentApplicationStatus.InterviewScheduled,
            ConflictOfInterestDeclared = true
        };
        Assert.False(SelectionWorkflowRules.CanTransition(app, RecruitmentApplicationStatus.Assessed));
        app.AssessmentScore = 85m;
        app.CriteriaVersionId = Guid.NewGuid();
        Assert.True(SelectionWorkflowRules.CanTransition(app, RecruitmentApplicationStatus.Assessed));
    }

    [Fact]
    public void Ranked_Requires_Rank()
    {
        var app = new RecruitmentApplication
        {
            Status = RecruitmentApplicationStatus.Assessed,
            AssessmentScore = 90m,
            CriteriaVersionId = Guid.NewGuid()
        };
        Assert.False(SelectionWorkflowRules.CanTransition(app, RecruitmentApplicationStatus.Ranked));
        app.Rank = 1;
        Assert.True(SelectionWorkflowRules.CanTransition(app, RecruitmentApplicationStatus.Ranked));
    }
}
