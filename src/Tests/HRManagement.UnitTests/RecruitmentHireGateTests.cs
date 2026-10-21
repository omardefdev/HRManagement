using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

public class RecruitmentHireGateTests
{
    [Fact]
    public void CanHire_Standard_Requires_OfferAccepted()
    {
        var app = new RecruitmentApplication
        {
            Status = RecruitmentApplicationStatus.OfferAccepted,
            RequisitionId = Guid.NewGuid(),
            ActiveOfferLetter = new OfferLetter { Status = OfferStatus.Accepted }
        };
        Assert.True(RecruitmentWorkflowRules.CanHire(app, legacyHireWithoutOffer: false));

        app.Status = RecruitmentApplicationStatus.Shortlisted;
        Assert.False(RecruitmentWorkflowRules.CanHire(app, legacyHireWithoutOffer: false));
    }

    [Fact]
    public void CanHire_Legacy_Allows_Shortlisted_Without_Requisition()
    {
        var app = new RecruitmentApplication
        {
            Status = RecruitmentApplicationStatus.Shortlisted,
            RequisitionId = null
        };
        Assert.True(RecruitmentWorkflowRules.CanHire(app, legacyHireWithoutOffer: true));
    }
}
