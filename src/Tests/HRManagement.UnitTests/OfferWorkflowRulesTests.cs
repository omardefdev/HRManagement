using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

public class OfferWorkflowRulesTests
{
    [Fact]
    public void CanAccept_Only_From_Issued()
    {
        var offer = new HR.Core.Domain.Entities.OfferLetter
        {
            Status = OfferStatus.Issued,
            ExpiryDate = DateTime.UtcNow.AddDays(7)
        };
        Assert.True(OfferWorkflowRules.CanAccept(offer));
        offer.Status = OfferStatus.Draft;
        Assert.False(OfferWorkflowRules.CanAccept(offer));
    }

    [Fact]
    public void CanSubmitForApproval_Only_From_Draft()
    {
        var offer = new HR.Core.Domain.Entities.OfferLetter { Status = OfferStatus.Draft };
        Assert.True(OfferWorkflowRules.CanSubmitForApproval(offer));
        offer.Status = OfferStatus.Issued;
        Assert.False(OfferWorkflowRules.CanSubmitForApproval(offer));
    }
}
