using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-004: selection pipeline transitions and gates.</summary>
public static class SelectionWorkflowRules
{
    private static readonly Dictionary<RecruitmentApplicationStatus, HashSet<RecruitmentApplicationStatus>> AllowedTransitions = new()
    {
        [RecruitmentApplicationStatus.Submitted] =
            [RecruitmentApplicationStatus.UnderReview, RecruitmentApplicationStatus.Rejected, RecruitmentApplicationStatus.Withdrawn],
        [RecruitmentApplicationStatus.UnderReview] =
            [RecruitmentApplicationStatus.Shortlisted, RecruitmentApplicationStatus.Rejected, RecruitmentApplicationStatus.Withdrawn],
        [RecruitmentApplicationStatus.Shortlisted] =
            [RecruitmentApplicationStatus.InterviewScheduled, RecruitmentApplicationStatus.Rejected, RecruitmentApplicationStatus.Withdrawn],
        [RecruitmentApplicationStatus.InterviewScheduled] =
            [RecruitmentApplicationStatus.Assessed, RecruitmentApplicationStatus.Rejected, RecruitmentApplicationStatus.Withdrawn],
        [RecruitmentApplicationStatus.Assessed] =
            [RecruitmentApplicationStatus.Ranked, RecruitmentApplicationStatus.Rejected, RecruitmentApplicationStatus.Withdrawn],
        [RecruitmentApplicationStatus.Ranked] =
            [RecruitmentApplicationStatus.OfferExtended, RecruitmentApplicationStatus.ReserveListed, RecruitmentApplicationStatus.Rejected, RecruitmentApplicationStatus.Withdrawn],
        [RecruitmentApplicationStatus.ReserveListed] =
            [RecruitmentApplicationStatus.OfferExtended, RecruitmentApplicationStatus.Rejected, RecruitmentApplicationStatus.Withdrawn],
        [RecruitmentApplicationStatus.OfferExtended] =
            [RecruitmentApplicationStatus.OfferAccepted, RecruitmentApplicationStatus.OfferDeclined, RecruitmentApplicationStatus.Rejected, RecruitmentApplicationStatus.Withdrawn],
        [RecruitmentApplicationStatus.OfferAccepted] =
            [RecruitmentApplicationStatus.Hired, RecruitmentApplicationStatus.Withdrawn],
        [RecruitmentApplicationStatus.Rejected] =
            [RecruitmentApplicationStatus.UnderReview],
        [RecruitmentApplicationStatus.Hired] = [],
        [RecruitmentApplicationStatus.Withdrawn] = [],
        [RecruitmentApplicationStatus.OfferDeclined] = []
    };

    /// <summary>HCM-026: «مُقيَّم» is reached only by RecruitmentScoringService once assessments are complete.</summary>
    public const string AssessedIsAutomaticMessage = "يتم الانتقال إلى «مُقيَّم» آلياً بعد اكتمال التقييمات.";

    /// <summary>HCM-027: «عرض مرفوض/منتهٍ» and «قائمة احتياط» are set only by offer outcomes and the reserve list.</summary>
    public const string OfferOutcomeIsAutomaticMessage = "تُضبط هذه الحالة آلياً من نتيجة العرض أو قائمة الاحتياط.";

    /// <summary>HCM-026/027: targets that are never offered or accepted as manual transitions.</summary>
    public static readonly IReadOnlySet<RecruitmentApplicationStatus> SystemOnlyTargets =
        new HashSet<RecruitmentApplicationStatus>
        {
            RecruitmentApplicationStatus.Assessed,
            RecruitmentApplicationStatus.OfferDeclined,
            RecruitmentApplicationStatus.ReserveListed
        };

    /// <summary>
    /// HCM-027: statuses that end an application. Cancel/close cascades and hire sibling rejection skip them;
    /// every other status (including ReserveListed) is open.
    /// </summary>
    public static readonly IReadOnlySet<RecruitmentApplicationStatus> TerminalStatuses =
        new HashSet<RecruitmentApplicationStatus>
        {
            RecruitmentApplicationStatus.Hired,
            RecruitmentApplicationStatus.Rejected,
            RecruitmentApplicationStatus.Withdrawn,
            RecruitmentApplicationStatus.OfferDeclined
        };

    public static string SystemOnlyTargetMessage(RecruitmentApplicationStatus to) =>
        to == RecruitmentApplicationStatus.Assessed ? AssessedIsAutomaticMessage : OfferOutcomeIsAutomaticMessage;

    public static bool CanTransition(
        RecruitmentApplication application,
        RecruitmentApplicationStatus to,
        bool legacyHireWithoutOffer = false)
    {
        if (!AllowedTransitions.TryGetValue(application.Status, out var targets) || !targets.Contains(to))
        {
            if (legacyHireWithoutOffer
                && application.Status == RecruitmentApplicationStatus.Shortlisted
                && to == RecruitmentApplicationStatus.Hired
                && application.RequisitionId is null
                && application.CompetitionId is null)
                return true;

            return false;
        }

        return ValidateTransitionGate(application, to) is null;
    }

    public static string? ValidateTransitionGate(RecruitmentApplication application, RecruitmentApplicationStatus to)
    {
        if (to == RecruitmentApplicationStatus.Shortlisted && !application.ConflictOfInterestDeclared)
            return "يجب إقرار عدم تعارض المصالح قبل الإدراج في القائمة المختصرة.";

        if (to == RecruitmentApplicationStatus.Assessed)
        {
            if (application.AssessmentScore is null)
                return "يجب تسجيل درجة التقييم.";
            if (application.CriteriaVersionId is null)
                return "يجب اختيار إصدار معايير التقييم.";
        }

        if (to == RecruitmentApplicationStatus.Ranked && application.Rank is null)
            return "يجب تعيين ترتيب المرشح.";

        if (!RecruitmentAppealWorkflowRules.CanRank(application)
            && to is RecruitmentApplicationStatus.Ranked or RecruitmentApplicationStatus.OfferExtended)
            return "لا يمكن الترتيب أو إصدار العرض أثناء استئناف معلق.";

        if (!RecruitmentAppealWorkflowRules.CanOffer(application)
            && to == RecruitmentApplicationStatus.OfferExtended)
            return "لا يمكن إصدار العرض أثناء استئناف معلق.";

        if (to == RecruitmentApplicationStatus.OfferExtended && application.ActiveOfferLetterId is null)
            return "يجب إنشاء مسودة خطاب عرض أولاً.";

        if (to == RecruitmentApplicationStatus.OfferAccepted)
        {
            if (application.ActiveOfferLetter is { Status: not OfferStatus.Accepted })
                return "يجب قبول خطاب العرض أولاً.";
        }

        return null;
    }

    public static IReadOnlyList<RecruitmentApplicationStatus> GetAllowedTargets(
        RecruitmentApplication application,
        bool legacyHireWithoutOffer = false)
    {
        if (!AllowedTransitions.TryGetValue(application.Status, out var targets))
            return [];

        var list = targets
            .Where(t => !SystemOnlyTargets.Contains(t))
            .Where(t => ValidateTransitionGate(application, t) is null)
            .ToList();

        if (legacyHireWithoutOffer
            && application.Status == RecruitmentApplicationStatus.Shortlisted
            && application.RequisitionId is null
            && application.CompetitionId is null)
            list.Add(RecruitmentApplicationStatus.Hired);

        return list;
    }
}
