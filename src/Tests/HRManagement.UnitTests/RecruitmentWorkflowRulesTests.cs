using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

public class RecruitmentWorkflowRulesTests
{
    [Fact]
    public void CanHire_Legacy_Overload_Allows_Shortlisted()
    {
        Assert.True(RecruitmentWorkflowRules.CanHire(RecruitmentApplicationStatus.Shortlisted));
        Assert.False(RecruitmentWorkflowRules.CanHire(RecruitmentApplicationStatus.Submitted));
    }

    [Fact]
    public void CanHire_Standard_Requires_OfferAccepted_With_Requisition()
    {
        var app = new RecruitmentApplication
        {
            Status = RecruitmentApplicationStatus.OfferAccepted,
            RequisitionId = Guid.NewGuid(),
            ActiveOfferLetter = new HR.Core.Domain.Entities.OfferLetter { Status = OfferStatus.Accepted }
        };
        Assert.True(RecruitmentWorkflowRules.CanHire(app, legacyHireWithoutOffer: false));
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
