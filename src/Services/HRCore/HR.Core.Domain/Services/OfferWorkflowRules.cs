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
}
