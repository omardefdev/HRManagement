using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>
/// HCM-027 (G-07): who may receive an offer. Offers follow merit order (the best available rank on the
/// requisition); skipping it is an exception that needs a written reason and the override approval workflow.
/// </summary>
public static class OfferEligibilityRules
{
    public const int MinMeritOverrideReasonLength = 30;

    public const string NotRankedMessage = "يمكن إصدار العرض للمرشحين المرتبين فقط.";
    public const string PendingAppealMessage = "لا يمكن إنشاء العرض أثناء استئناف معلق.";
    public const string OtherActiveOfferMessage = "يوجد عرض نشط على هذا الشاغر.";

    public static readonly string MeritOverrideReasonMessage =
        $"تجاوز ترتيب الجدارة يتطلب مبرراً مكتوباً لا يقل عن {MinMeritOverrideReasonLength} حرفاً.";

    /// <summary>Statuses from which an offer can be drafted.</summary>
    public static readonly IReadOnlySet<RecruitmentApplicationStatus> OfferableStatuses =
        new HashSet<RecruitmentApplicationStatus>
        {
            RecruitmentApplicationStatus.Ranked,
            RecruitmentApplicationStatus.ReserveListed
        };

    /// <summary>Offer statuses that occupy the requisition (one active offer per requisition).</summary>
    public static bool OccupiesRequisition(OfferLetter offer) =>
        offer.Status is OfferStatus.Draft or OfferStatus.PendingApproval or OfferStatus.Issued
        || (offer.Status == OfferStatus.Accepted && !offer.IsConvertedToEmployee);

    /// <summary>An application counts towards the best available rank.</summary>
    public static bool IsAvailableForMerit(RecruitmentApplication application) =>
        OfferableStatuses.Contains(application.Status)
        && application.Rank is not null
        && !RecruitmentAppealWorkflowRules.HasPendingAppeal(application);

    public static string NotBestRankMessage(int? bestAvailableRank) =>
        $"المرشح ليس الأعلى رتبة المتاح (الرتبة المتاحة: {bestAvailableRank?.ToString() ?? "—"}).";

    public static string? Validate(
        RecruitmentApplication app,
        int? bestAvailableRank,
        bool requisitionHasOtherActiveOffer,
        bool isMeritOverride,
        string? meritOverrideReason)
    {
        if (!OfferableStatuses.Contains(app.Status))
            return NotRankedMessage;

        if (!RecruitmentAppealWorkflowRules.CanOffer(app))
            return PendingAppealMessage;

        if (requisitionHasOtherActiveOffer)
            return OtherActiveOfferMessage;

        if (app.Rank is null || app.Rank != bestAvailableRank)
        {
            if (!isMeritOverride)
                return NotBestRankMessage(bestAvailableRank);
        }

        if (isMeritOverride
            && (meritOverrideReason?.Trim().Length ?? 0) < MinMeritOverrideReasonLength)
            return MeritOverrideReasonMessage;

        return null;
    }
}
