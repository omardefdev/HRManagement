using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel;

namespace HR.Core.Domain.Services;

/// <summary>Why a candidate application pair is (not) due for retention anonymization (HCM-030 G1).</summary>
public enum CandidateRetentionVerdict
{
    /// <summary>Retention elapsed and nothing holds it: anonymize (or complete a half-anonymized pair).</summary>
    Due,
    NotYetDue,
    OpenRequisition,
    Hired,
    AlreadyAnonymized,
    LegalHold,
    PendingAppeal
}

/// <summary>HCM-006: legal hold, anonymize, and purge eligibility for candidate applications.</summary>
public static class CandidateApplicationLegalLifecycleService
{
    /// <summary>
    /// HCM-030 (G1): default of <c>Recruitment:Retention:Months</c> (GOV-001 <c>ret-recruitment-application</c>: 3 years).
    /// The Domain project cannot read options, so callers pass the configured value.
    /// </summary>
    public const int DefaultRetentionMonths = 36;

    /// <summary>
    /// HCM-030 (G1): a trigger date is past retention when it is on or before <c>utcNow − months</c>. Expressed as a cutoff so
    /// the same rule translates to SQL (the job and the admin list filter on it).
    /// </summary>
    public static DateTime RetentionCutoff(DateTime utcNow, int retentionMonths = DefaultRetentionMonths) =>
        utcNow.AddMonths(-Math.Max(1, retentionMonths));

    public static (bool Success, string? Error) SetLegalHold(
        CitizenApplication application,
        bool hold,
        string? reason)
    {
        if (application.AnonymizedAt.HasValue)
            return (false, "الطلب مُخفى الهوية مسبقاً.");

        application.LegalHold = hold;
        if (!hold && string.IsNullOrWhiteSpace(reason))
            return (true, null);

        return (true, null);
    }

    public static (bool Success, string? Error) SetLegalHold(
        RecruitmentApplication application,
        bool hold)
    {
        if (application.AnonymizedAt.HasValue)
            return (false, "الطلب مُخفى الهوية مسبقاً.");

        application.LegalHold = hold;
        return (true, null);
    }

    /// <summary>
    /// Legacy rule for an unlinked citizen row (no recruitment application): trigger = requisition <c>ClosedDate</c> ??
    /// <c>SubmittedAt</c>. A linked row is never purged alone — <see cref="EvaluatePair"/> handles the pair.
    /// </summary>
    public static bool CanPurge(
        CitizenApplication application,
        RecruitmentRequisition? requisition,
        DateTime utcNow,
        int retentionMonths = DefaultRetentionMonths)
    {
        if (application.LegalHold || application.AnonymizedAt.HasValue)
            return false;

        // HCM-030 (G1): a linked row belongs to a pair (channel row + recruitment application), anonymized together by
        // CandidateRetentionService through EvaluatePair.
        if (application.PromotedRecruitmentApplicationId.HasValue)
            return false;

        var trigger = requisition?.ClosedDate ?? application.SubmittedAt;
        return trigger <= RetentionCutoff(utcNow, retentionMonths);
    }

    /// <summary>The recruitment application alone (no channel row); see <see cref="EvaluatePair"/>.</summary>
    public static bool CanPurge(
        RecruitmentApplication application,
        RecruitmentRequisition? requisition,
        DateTime utcNow,
        int retentionMonths = DefaultRetentionMonths) =>
        EvaluatePair(application, requisition, citizen: null, hasPendingAppeal: false, utcNow, retentionMonths)
            == CandidateRetentionVerdict.Due;

    /// <summary>
    /// HCM-030 (G1): trigger date of a pair — requisition <c>ClosedDate</c> ?? <c>CancelledAt</c> ?? the application's
    /// <c>AppliedAt</c>; null while the requisition is open (open requisitions never trigger).
    /// </summary>
    public static DateTime? RetentionTrigger(RecruitmentApplication application, RecruitmentRequisition? requisition)
    {
        if (requisition is not null
            && requisition.Status is not (RequisitionStatus.Closed or RequisitionStatus.Cancelled))
            return null;
        return requisition?.ClosedDate ?? requisition?.CancelledAt ?? application.AppliedAt;
    }

    /// <summary>
    /// HCM-030 (G1): the pair rule. A pair is the recruitment application plus its linked citizen row (internal rows carry
    /// no hold or anonymization state). Due when not hired, no legal hold on either row, no pending appeal, not fully
    /// anonymized (a pair where only one row was anonymized — e.g. a DSAR erasure of the citizen row — is completed), and
    /// the trigger date is past <c>Recruitment:Retention:Months</c>.
    /// </summary>
    /// <param name="residualData">
    /// Both rows are anonymized but data is left behind (documents, messages, fields cleared only since HCM-030): the pair
    /// is completed instead of being reported as already anonymized.
    /// </param>
    public static CandidateRetentionVerdict EvaluatePair(
        RecruitmentApplication application,
        RecruitmentRequisition? requisition,
        CitizenApplication? citizen,
        bool hasPendingAppeal,
        DateTime utcNow,
        int retentionMonths = DefaultRetentionMonths,
        bool residualData = false)
    {
        if (application.HiredEmployeeId.HasValue)
            return CandidateRetentionVerdict.Hired;

        if (!residualData && application.AnonymizedAt.HasValue && (citizen is null || citizen.AnonymizedAt.HasValue))
            return CandidateRetentionVerdict.AlreadyAnonymized;

        if (RetentionTrigger(application, requisition) is not DateTime trigger)
            return CandidateRetentionVerdict.OpenRequisition;

        if (trigger > RetentionCutoff(utcNow, retentionMonths))
            return CandidateRetentionVerdict.NotYetDue;

        if (application.LegalHold || citizen?.LegalHold == true)
            return CandidateRetentionVerdict.LegalHold;

        if (hasPendingAppeal || application.AppealStatus == RecruitmentAppealStatus.Pending)
            return CandidateRetentionVerdict.PendingAppeal;

        return CandidateRetentionVerdict.Due;
    }

    /// <summary>
    /// Anonymizes the citizen row: identity, contact and consent, cover note and the HCM-030 profile. Idempotent: an
    /// already anonymized row keeps its date and placeholders but any field left behind (rows anonymized before HCM-030)
    /// is cleared too.
    /// </summary>
    public static (bool Success, string? Error) TryAnonymize(CitizenApplication application, DateTime? utcNow = null)
    {
        if (application.LegalHold)
            return (false, "لا يمكن إخفاء الهوية أثناء الحجز القانوني.");

        var token = application.Id.ToString("N")[..8].ToUpperInvariant();
        application.FullName = $"ANON {token}";
        application.NationalId = $"ANON-{token}";
        application.NationalIdHash = PiiMask.Sha256Hex(application.NationalId);
        application.Phone = null;
        application.Email = null;
        application.NotificationChannel = null;
        application.NotificationConsentAt = null;
        application.CoverNote = null;
        application.BirthDate = null;
        application.EducationLevelCode = null;
        application.Specialization = null;
        application.GraduationYear = null;
        application.YearsOfExperience = null;
        application.AnonymizedAt ??= utcNow ?? DateTime.UtcNow;
        return (true, null);
    }

    /// <summary>
    /// Anonymizes the recruitment application: identity, notes, the HCM-030 profile snapshot, the rejection note and the
    /// eligibility override reason. Idempotent like the citizen overload.
    /// </summary>
    public static (bool Success, string? Error) TryAnonymize(RecruitmentApplication application, DateTime? utcNow = null)
    {
        if (application.LegalHold)
            return (false, "لا يمكن إخفاء الهوية أثناء الحجز القانوني.");

        var token = application.Id.ToString("N")[..8].ToUpperInvariant();
        application.ApplicantFullName = $"ANON {token}";
        application.NationalId = $"ANON-{token}";
        application.NationalIdHash = PiiMask.Sha256Hex(application.NationalId);
        application.Notes = null;
        application.AppealNotes = null;
        application.RejectionNote = null;
        application.EligibilityOverrideReason = null;
        application.BirthDate = null;
        application.EducationLevelCode = null;
        application.Specialization = null;
        application.GraduationYear = null;
        application.YearsOfExperience = null;
        application.AnonymizedAt ??= utcNow ?? DateTime.UtcNow;
        return (true, null);
    }

    /// <summary>
    /// HCM-030 (G2): an internal channel row has no hold or anonymization state of its own (the employee stays an
    /// employee); only its free text is cleared when the pair is anonymized.
    /// </summary>
    public static void ClearFreeText(InternalApplication application) => application.Motivation = null;
}
