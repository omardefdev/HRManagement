using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel;

namespace HR.Core.Domain.Services;

/// <summary>HCM-006: legal hold, anonymize, and purge eligibility for candidate applications.</summary>
public static class CandidateApplicationLegalLifecycleService
{
    public const int RetentionYears = 3;

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

    public static bool CanPurge(CitizenApplication application, RecruitmentRequisition? requisition, DateTime utcNow)
    {
        if (application.LegalHold || application.AnonymizedAt.HasValue)
            return false;

        // PLAN-30 S2: since HCM-030 every requisition-linked citizen row is linked, so this never purges them — retention
        // must handle the pair (channel row + recruitment application) together.
        if (application.PromotedRecruitmentApplicationId.HasValue)
            return false;

        var trigger = requisition?.ClosedDate ?? application.SubmittedAt;
        return utcNow >= trigger.AddYears(RetentionYears);
    }

    public static bool CanPurge(RecruitmentApplication application, RecruitmentRequisition? requisition, DateTime utcNow)
    {
        if (application.LegalHold || application.AnonymizedAt.HasValue)
            return false;

        if (application.HiredEmployeeId.HasValue)
            return false;

        if (application.AppealStatus == RecruitmentAppealStatus.Pending)
            return false;

        var trigger = requisition?.ClosedDate ?? application.AppliedAt;
        return utcNow >= trigger.AddYears(RetentionYears);
    }

    public static (bool Success, string? Error) TryAnonymize(CitizenApplication application)
    {
        if (application.LegalHold)
            return (false, "لا يمكن إخفاء الهوية أثناء الحجز القانوني.");

        if (application.AnonymizedAt.HasValue)
            return (true, null);

        var token = application.Id.ToString("N")[..8].ToUpperInvariant();
        application.FullName = $"ANON {token}";
        application.NationalId = $"ANON-{token}";
        application.NationalIdHash = PiiMask.Sha256Hex(application.NationalId);
        application.Phone = null;
        application.Email = null;
        application.CoverNote = null;
        // PLAN-30 S2: also clear the HCM-030 profile (BirthDate, EducationLevelCode, Specialization, …).
        application.AnonymizedAt = DateTime.UtcNow;
        return (true, null);
    }

    public static (bool Success, string? Error) TryAnonymize(RecruitmentApplication application)
    {
        if (application.LegalHold)
            return (false, "لا يمكن إخفاء الهوية أثناء الحجز القانوني.");

        if (application.AnonymizedAt.HasValue)
            return (true, null);

        var token = application.Id.ToString("N")[..8].ToUpperInvariant();
        application.ApplicantFullName = $"ANON {token}";
        application.NationalId = $"ANON-{token}";
        application.NationalIdHash = PiiMask.Sha256Hex(application.NationalId);
        application.Notes = null;
        application.AppealNotes = null;
        // PLAN-30 S2: also clear the HCM-030 profile snapshot and EligibilityOverrideReason.
        application.AnonymizedAt = DateTime.UtcNow;
        return (true, null);
    }
}
