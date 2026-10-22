using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-006 / HCM-028: appeal gates for recruitment selection.</summary>
public static class RecruitmentAppealWorkflowRules
{
    public const int DefaultAppealWindowDays = 14;
    public const int MinReasonLength = 20;
    public const int MinRationaleLength = 30;
    public const int MinInfoTextLength = 10;

    public static readonly IReadOnlyList<RecruitmentRejectionKind> DefaultAppealableKinds =
        [RecruitmentRejectionKind.SelectionDecision, RecruitmentRejectionKind.NotSelectedOnHire, RecruitmentRejectionKind.OfferRevoked];

    /// <summary>Outcomes the reviewer may choose (LegacyReopen only describes migrated history).</summary>
    public static readonly IReadOnlyList<RecruitmentAppealOutcome> DecidableOutcomes =
    [
        RecruitmentAppealOutcome.Upheld,
        RecruitmentAppealOutcome.UpheldRankCorrection,
        RecruitmentAppealOutcome.Dismissed,
        RecruitmentAppealOutcome.UpheldNoRemedy
    ];

    /// <summary>On a closed or cancelled requisition the vacancy can no longer be remedied.</summary>
    public static readonly IReadOnlyList<RecruitmentAppealOutcome> NoVacancyOutcomes =
        [RecruitmentAppealOutcome.Dismissed, RecruitmentAppealOutcome.UpheldNoRemedy];

    public const string NotRejectedMessage = "يمكن الاستئناف على الطلبات المرفوضة فقط.";
    public const string AlreadyAppealedMessage = "يوجد استئناف سابق على هذا الرفض.";
    public const string WindowExpiredMessage = "انقضت مهلة الاستئناف على هذا الرفض.";
    public const string KindNotAppealableMessage = "لا يقبل هذا النوع من الرفض الاستئناف.";
    public const string NotIndependentMessage = "يجب أن يكون المراجع مستقلاً عن اللجنة والمقابلة والتقييم.";
    public const string NotAssignedReviewerMessage = "غير مصرح: يجب أن تكون المراجع المستقل المعيّن.";
    public const string NoVacancyOutcomeMessage =
        "الشاغر مغلق أو ملغى — النتائج المسموحة: رفض الاستئناف أو قبول دون أثر على الشاغر.";
    public const string RankCorrectionNotAllowedMessage =
        "تصحيح الترتيب يتطلب أن يكون للمرشح ترتيب قبل الرفض وأن يكون الشاغر منشوراً.";
    public const string HireBlockedMessage = "يوجد استئناف معلق على هذا الشاغر — يجب البت فيه قبل التعيين.";

    public static bool HasPendingAppeal(RecruitmentApplication application) =>
        application.AppealStatus == RecruitmentAppealStatus.Pending;

    public static bool CanRank(RecruitmentApplication application) =>
        !HasPendingAppeal(application);

    public static bool CanOffer(RecruitmentApplication application) =>
        !HasPendingAppeal(application);

    /// <summary>Legacy rows (rejected before HCM-028) have no kind and count as a selection decision.</summary>
    public static RecruitmentRejectionKind EffectiveRejectionKind(RecruitmentApplication application) =>
        application.RejectionKind ?? RecruitmentRejectionKind.SelectionDecision;

    /// <summary>HCM-028 (G3): the window runs from <c>RejectedAt</c> (legacy rows: <c>ReviewedAt</c>).</summary>
    public static string? GetAppealError(
        RecruitmentApplication application,
        DateTime utcNow,
        int appealWindowDays = DefaultAppealWindowDays,
        IReadOnlyCollection<RecruitmentRejectionKind>? appealableKinds = null)
    {
        if (application.Status != RecruitmentApplicationStatus.Rejected)
            return NotRejectedMessage;

        if (application.AppealStatus is RecruitmentAppealStatus.Pending or RecruitmentAppealStatus.Decided)
            return AlreadyAppealedMessage;

        var kinds = appealableKinds is { Count: > 0 } ? appealableKinds : DefaultAppealableKinds;
        if (!kinds.Contains(EffectiveRejectionKind(application)))
            return KindNotAppealableMessage;

        var rejectedAt = application.RejectedAt ?? application.ReviewedAt;
        if (rejectedAt is not null && utcNow > rejectedAt.Value.AddDays(appealWindowDays))
            return WindowExpiredMessage;

        return null;
    }

    public static bool CanAppeal(
        RecruitmentApplication application,
        DateTime utcNow,
        int appealWindowDays = DefaultAppealWindowDays,
        IReadOnlyCollection<RecruitmentRejectionKind>? appealableKinds = null) =>
        GetAppealError(application, utcNow, appealWindowDays, appealableKinds) is null;

    /// <summary>
    /// HCM-028 (H3): only the assigned reviewer decides — by employee (<c>employee_id</c> claim), or for legacy rows
    /// without one, by the Keycloak user id typed at filing. The actor must hold the compliance role (or Admin).
    /// </summary>
    public static bool IsAssignedReviewer(
        RecruitmentAppeal appeal,
        bool actorHasReviewerRole,
        Guid? actorUserId,
        Guid? actorEmployeeId)
    {
        if (!actorHasReviewerRole || appeal.Status != RecruitmentAppealStatus.Pending)
            return false;

        if (appeal.IndependentReviewerEmployeeId is Guid reviewerEmployeeId)
            return actorEmployeeId == reviewerEmployeeId;

        return actorUserId.HasValue && appeal.IndependentReviewerId == actorUserId;
    }

    public static string? ValidateOutcome(
        RecruitmentAppealOutcome outcome,
        RequisitionStatus? requisitionStatus,
        int? rankBeforeRejection,
        int? correctedRank)
    {
        if (!DecidableOutcomes.Contains(outcome))
            return "نتيجة الاستئناف غير صالحة.";

        if (requisitionStatus is RequisitionStatus.Closed or RequisitionStatus.Cancelled
            && !NoVacancyOutcomes.Contains(outcome))
            return NoVacancyOutcomeMessage;

        if (outcome == RecruitmentAppealOutcome.UpheldRankCorrection)
        {
            if (correctedRank is not >= 1)
                return "الترتيب المصحّح يجب أن يكون موجباً.";
            if (rankBeforeRejection is null || requisitionStatus != RequisitionStatus.Published)
                return RankCorrectionNotAllowedMessage;
        }

        return null;
    }

    public static bool HasQuorum(IEnumerable<SelectionCommitteeMember> members, int minimum = 3)
    {
        var active = members.Count(m => !m.Recused);
        return active >= minimum;
    }
}
