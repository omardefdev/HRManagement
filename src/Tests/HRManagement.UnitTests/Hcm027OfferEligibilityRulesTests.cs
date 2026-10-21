using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>HCM-027 (A1): pure offer eligibility and merit order rules.</summary>
public class Hcm027OfferEligibilityRulesTests
{
    private const string ValidReason = "المرشح الأول اعتذر شفهياً واللجنة أوصت بالانتقال للمرشح التالي";

    private static RecruitmentApplication App(
        RecruitmentApplicationStatus status = RecruitmentApplicationStatus.Ranked,
        int? rank = 1,
        RecruitmentAppealStatus appeal = RecruitmentAppealStatus.None) =>
        new() { ApplicantFullName = "مرشح", Status = status, Rank = rank, AppealStatus = appeal };

    [Fact]
    public void Assessed_is_rejected()
    {
        var error = OfferEligibilityRules.Validate(App(RecruitmentApplicationStatus.Assessed), 1, false, false, null);
        Assert.Equal(OfferEligibilityRules.NotRankedMessage, error);
    }

    [Fact]
    public void Ranked_best_is_accepted()
    {
        Assert.Null(OfferEligibilityRules.Validate(App(), 1, false, false, null));
    }

    [Fact]
    public void ReserveListed_best_is_accepted()
    {
        Assert.Null(OfferEligibilityRules.Validate(App(RecruitmentApplicationStatus.ReserveListed, 3), 3, false, false, null));
    }

    [Fact]
    public void Rank_two_without_override_is_rejected()
    {
        var error = OfferEligibilityRules.Validate(App(rank: 2), 1, false, false, null);
        Assert.Equal(OfferEligibilityRules.NotBestRankMessage(1), error);
        Assert.Contains("1", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("سبب قصير")]
    [InlineData("                                          ")]
    public void Rank_two_with_override_and_short_reason_is_rejected(string? reason)
    {
        var error = OfferEligibilityRules.Validate(App(rank: 2), 1, false, true, reason);
        Assert.Equal(OfferEligibilityRules.MeritOverrideReasonMessage, error);
    }

    [Fact]
    public void Rank_two_with_override_and_valid_reason_is_accepted()
    {
        Assert.True(ValidReason.Length >= OfferEligibilityRules.MinMeritOverrideReasonLength);
        Assert.Null(OfferEligibilityRules.Validate(App(rank: 2), 1, false, true, ValidReason));
    }

    [Fact]
    public void Pending_appeal_is_rejected()
    {
        var error = OfferEligibilityRules.Validate(App(appeal: RecruitmentAppealStatus.Pending), 1, false, false, null);
        Assert.Equal(OfferEligibilityRules.PendingAppealMessage, error);
    }

    [Fact]
    public void Other_active_offer_is_rejected_even_with_override()
    {
        Assert.Equal(OfferEligibilityRules.OtherActiveOfferMessage,
            OfferEligibilityRules.Validate(App(), 1, true, false, null));
        Assert.Equal(OfferEligibilityRules.OtherActiveOfferMessage,
            OfferEligibilityRules.Validate(App(rank: 2), 1, true, true, ValidReason));
    }

    [Theory]
    [InlineData(OfferStatus.Draft, false, true)]
    [InlineData(OfferStatus.PendingApproval, false, true)]
    [InlineData(OfferStatus.Issued, false, true)]
    [InlineData(OfferStatus.Accepted, false, true)]
    [InlineData(OfferStatus.Accepted, true, false)]
    [InlineData(OfferStatus.Declined, false, false)]
    [InlineData(OfferStatus.Expired, false, false)]
    [InlineData(OfferStatus.Revoked, false, false)]
    public void Occupying_offer_statuses(OfferStatus status, bool converted, bool expected)
    {
        var offer = new OfferLetter { Status = status, IsConvertedToEmployee = converted };
        Assert.Equal(expected, OfferEligibilityRules.OccupiesRequisition(offer));
    }
}
