using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-004: offer letter lifecycle rules.</summary>
public static class OfferWorkflowRules
{
    private static readonly Dictionary<OfferStatus, HashSet<OfferStatus>> AllowedTransitions = new()
    {
        [OfferStatus.Draft] = [OfferStatus.PendingApproval, OfferStatus.Revoked],
        [OfferStatus.PendingApproval] = [OfferStatus.Issued, OfferStatus.Revoked],
        [OfferStatus.Issued] = [OfferStatus.Accepted, OfferStatus.Declined, OfferStatus.Expired, OfferStatus.Revoked],
        [OfferStatus.Accepted] = [],
        [OfferStatus.Declined] = [],
        [OfferStatus.Expired] = [],
        [OfferStatus.Revoked] = []
    };

    public static bool CanTransition(OfferStatus from, OfferStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static bool CanSubmitForApproval(OfferLetter offer) =>
        offer.Status == OfferStatus.Draft;

    public static bool CanIssue(OfferLetter offer) =>
        offer.Status == OfferStatus.PendingApproval;

    public static bool CanAccept(OfferLetter offer) =>
        offer.Status == OfferStatus.Issued
        && (offer.ExpiryDate is null || offer.ExpiryDate >= DateTime.UtcNow);

    public static bool CanDecline(OfferLetter offer) =>
        offer.Status == OfferStatus.Issued;

    public static bool IsActiveOffer(OfferLetter offer) =>
        offer.Status is OfferStatus.Issued or OfferStatus.Accepted;

    /// <summary>HCM-027: allowed range for <see cref="OfferLetter.ValidityDays"/>.</summary>
    public const int MinValidityDays = 3;
    public const int MaxValidityDays = 60;

    public const int MinDeclineReasonLength = 5;
    public const int MinRevokeReasonLength = 10;

    public static readonly string ValidityDaysRangeMessage =
        $"مدة صلاحية العرض يجب أن تكون بين {MinValidityDays} و{MaxValidityDays} يوماً.";

    public static bool IsValidityDaysInRange(int days) => days is >= MinValidityDays and <= MaxValidityDays;

    /// <summary>HCM-027: end of the UTC day <paramref name="validityDays"/> days after the issue date.</summary>
    public static DateTime ComputeExpiryDate(DateTime issuedUtc, int validityDays) =>
        DateTime.SpecifyKind(issuedUtc.Date.AddDays(validityDays + 1).AddSeconds(-1), DateTimeKind.Utc);

    /// <summary>HCM-027: every path that issues an offer sets the expiry from its validity here.</summary>
    public static void MarkIssued(OfferLetter offer, DateTime nowUtc)
    {
        offer.Status = OfferStatus.Issued;
        offer.IssuedDate = nowUtc;
        offer.ExpiryDate = ComputeExpiryDate(nowUtc, offer.ValidityDays);
    }

    public static bool CanRevoke(OfferLetter offer) =>
        CanTransition(offer.Status, OfferStatus.Revoked);

    public static bool CanExpire(OfferLetter offer, DateTime nowUtc) =>
        offer.Status == OfferStatus.Issued && offer.ExpiryDate is DateTime expiry && expiry < nowUtc;
}
