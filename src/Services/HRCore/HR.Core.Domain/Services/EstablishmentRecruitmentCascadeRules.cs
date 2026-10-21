using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>
/// HCM-026 (G-11): how establishment changes on a funded position affect its open requisition.
/// Freezing is blocked while an offer is out (Issued) or accepted but not yet converted to an employee;
/// otherwise the blocking requisition is cancelled automatically.
/// </summary>
public static class EstablishmentRecruitmentCascadeRules
{
    public const string FreezeBlockedByOfferMessage =
        "لا يمكن تجميد منصب عليه عرض توظيف صادر أو مقبول — ألغِ العرض أولاً.";

    public static bool BlocksFreeze(OfferLetter offer) =>
        offer.Status == OfferStatus.Issued
        || (offer.Status == OfferStatus.Accepted && !offer.IsConvertedToEmployee);

    public static string? ValidateFreeze(IEnumerable<OfferLetter> offersOnBlockingRequisitions) =>
        offersOnBlockingRequisitions.Any(BlocksFreeze) ? FreezeBlockedByOfferMessage : null;

    public static string FreezeCancellationReason(string? freezeReason) =>
        $"تجميد المنصب: {(string.IsNullOrWhiteSpace(freezeReason) ? "—" : freezeReason.Trim())}";
}
