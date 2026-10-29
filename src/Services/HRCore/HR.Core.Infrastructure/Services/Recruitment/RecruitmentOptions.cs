using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HRManagement.SharedKernel.Enums;
using HRManagement.ServiceDefaults;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-026: typed settings bound from the <c>Recruitment</c> configuration section.
/// Existing <c>IConfiguration</c> reads of <c>Recruitment:LegacyHireWithoutOffer</c> and
/// <c>Recruitment:RejectSiblingApplicationsOnHire</c> keep working; the properties below mirror them.
/// </summary>
public sealed class RecruitmentOptions
{
    public const string SectionName = "Recruitment";

    /// <summary>Mirrors <c>Recruitment:LegacyHireWithoutOffer</c> (default false).</summary>
    public bool LegacyHireWithoutOffer { get; set; }

    /// <summary>Mirrors <c>Recruitment:RejectSiblingApplicationsOnHire</c> (default true).</summary>
    public bool RejectSiblingApplicationsOnHire { get; set; } = true;

    /// <summary>Publishing (public or internal) requires an active criteria version.</summary>
    public bool RequireCriteriaBeforePublish { get; set; } = true;

    /// <summary>Complete assessors required before an application moves to Assessed.</summary>
    public int MinAssessors { get; set; } = 1;

    /// <summary>Maximum number of times an interview chain can be rescheduled.</summary>
    public int MaxInterviewReschedules { get; set; } = 1;

    /// <summary>
    /// HCM-026 (E1): at first publish the application closing date must be at least this many days after today (UTC).
    /// </summary>
    public int MinPostingDays { get; set; } = 7;

    /// <summary>
    /// HCM-027 (C4): default <c>OfferLetter.ValidityDays</c> for new offers (<c>Recruitment:OfferValidityDays</c>, default 14).
    /// The offer service accepts 3..60; the expiry is set when the offer is issued.
    /// </summary>
    public int OfferValidityDays { get; set; } = 14;

    /// <summary>
    /// PLAN-30 (F5): <c>Recruitment:OfferReminderHours</c> — an <c>OfferExpiryReminder</c> event (one per offer) is recorded
    /// for Issued offers that expire within this many hours (default 48; 0 or less turns the reminder off). Recorded by
    /// <c>CandidateNotificationDispatcher.RemindDueOffersAsync</c>.
    /// </summary>
    public int OfferReminderHours { get; set; } = 48;

    /// <summary>
    /// HCM-027 (E4): <c>Recruitment:MaxStartDays</c> — the proposed start date must be within this many days
    /// of today (UTC), default 90.
    /// </summary>
    public int MaxStartDays { get; set; } = 90;

    /// <summary>
    /// HCM-027 (F2, D-7): <c>Recruitment:ReserveValidityMonths</c> — ranked siblings stay on the reserve list
    /// this many months after a hire, default 6.
    /// </summary>
    public int ReserveValidityMonths { get; set; } = 6;

    /// <summary>
    /// HCM-027 (F3): <c>Recruitment:AutoDraftNextOffer</c> — after a decline/expiry/revoke, draft an offer for the
    /// next candidate by merit with the previous offer's terms (default false: only a suggestion is shown).
    /// </summary>
    public bool AutoDraftNextOffer { get; set; }

    /// <summary>
    /// HCM-028 (D-5): <c>Recruitment:CommitteeApprovalDefault</c> — initial value of
    /// <c>RecruitmentRequisition.RequiresCommitteeApproval</c> for new requisitions (default false;
    /// true in Production and Staging).
    /// </summary>
    public bool CommitteeApprovalDefault { get; set; }

    /// <summary>
    /// HCM-028 (G3): <c>Recruitment:AppealWindowDays</c> — an appeal is filed within this many days of
    /// <c>RejectedAt</c> (legacy rows: <c>ReviewedAt</c>), default 14.
    /// </summary>
    public int AppealWindowDays { get; set; } = RecruitmentAppealWorkflowRules.DefaultAppealWindowDays;

    /// <summary>
    /// HCM-028 (G3): <c>Recruitment:AppealableRejectionKinds</c> — rejection kinds that may be appealed. Null or empty
    /// means <see cref="RecruitmentAppealWorkflowRules.DefaultAppealableKinds"/> (SelectionDecision, NotSelectedOnHire,
    /// OfferRevoked); an array, so configuration replaces the default instead of appending to it.
    /// </summary>
    public RecruitmentRejectionKind[]? AppealableRejectionKinds { get; set; }

    /// <summary>
    /// HCM-028 (I): <c>Recruitment:BlockHireOnPendingAppeal</c> — no hire on a requisition while any of its
    /// applications has a pending appeal (default true). Offers stay allowed.
    /// </summary>
    public bool BlockHireOnPendingAppeal { get; set; } = true;

    /// <summary>Configuration key of <see cref="DefaultEmploymentType"/> (read through <c>IConfiguration</c> at hire).</summary>
    public const string DefaultEmploymentTypeKey = SectionName + ":" + nameof(DefaultEmploymentType);

    /// <summary>
    /// HCM-029 (B): <c>Recruitment:DefaultEmploymentType</c> — employment type pre-selected on the hire form (enum name,
    /// default <see cref="EmploymentType.Permanent"/>). Funded positions and the job catalog carry no employment type,
    /// so this is the only default; choosing another value needs a reason (audited).
    /// </summary>
    public EmploymentType DefaultEmploymentType { get; set; } = EmploymentType.Permanent;

    public const string DefaultOfferLetterConditionsAr =
        "يُعدّ هذا العرض نافذاً حتى تاريخ انتهاء صلاحيته، ويخضع التعيين لاستكمال الوثائق المطلوبة واللياقة الصحية " +
        "وصدور قرار التعيين وفق الأنظمة النافذة.";

    /// <summary>
    /// HCM-029 (D1): <c>Recruitment:OfferLetterConditionsAr</c> — conditions paragraph printed on the offer letter.
    /// It is part of the document hash, so changing it makes issued offers show the "data changed after issue" warning.
    /// </summary>
    public string OfferLetterConditionsAr { get; set; } = DefaultOfferLetterConditionsAr;

    public IReadOnlyCollection<RecruitmentRejectionKind> GetAppealableRejectionKinds() =>
        AppealableRejectionKinds is { Length: > 0 } kinds ? kinds : RecruitmentAppealWorkflowRules.DefaultAppealableKinds.ToArray();

    /// <summary>Background jobs settings (<c>Recruitment:Jobs:*</c>).</summary>
    public RecruitmentJobsOptions Jobs { get; set; } = new();

    /// <summary>HCM-030 (D3): eligibility check settings (<c>Recruitment:Eligibility:*</c>).</summary>
    public RecruitmentEligibilityOptions Eligibility { get; set; } = new();

    /// <summary>HCM-030 (E3): candidate document limits (<c>Recruitment:CandidateDocuments:*</c>).</summary>
    public RecruitmentCandidateDocumentsOptions CandidateDocuments { get; set; } = new();

    /// <summary>COM-030 (F2): public apply form settings (<c>Recruitment:Portal:*</c>).</summary>
    public RecruitmentPortalOptions Portal { get; set; } = new();

    /// <summary>
    /// COM-030 (F6): <c>Recruitment:PublicPortalBaseUrl</c> — public origin of the careers portal (e.g.
    /// <c>https://hr.example.gov.sy</c>) used for the links in candidate messages. Empty (default; Production/Staging until
    /// provisioned): messages carry no link — there is deliberately no fallback to <c>HrGateway:PublicBaseUrl</c>, which is
    /// localhost in the base settings.
    /// </summary>
    public string? PublicPortalBaseUrl { get; set; }

    /// <summary>
    /// COM-030 (F6): <c>{PublicPortalBaseUrl}/careers/track</c> (no query: the page asks for the reference and national id
    /// again), or null when <see cref="PublicPortalBaseUrl"/> is empty. PLAN-30 S3: switch to <c>/careers/my</c>.
    /// </summary>
    public string? PublicTrackUrl() =>
        string.IsNullOrWhiteSpace(PublicPortalBaseUrl)
            ? null
            : PublicPortalBaseUrl.Trim().TrimEnd('/') + ProductionHostExtensions.PublicCareersGatewayPrefix + "/track";
}

/// <summary>COM-030 (F2): settings of the public apply form.</summary>
public sealed class RecruitmentPortalOptions
{
    public const string DefaultPhoneCountryCode = "963";

    /// <summary>
    /// <c>Recruitment:Portal:PhoneCountryCode</c> — international dialling code (digits only, default <c>963</c>) used to
    /// normalize national phone numbers (<c>0999…</c> → <c>+963999…</c>).
    /// </summary>
    public string PhoneCountryCode { get; set; } = DefaultPhoneCountryCode;
}

/// <summary>
/// HCM-030 (E3): limits of the documents attached to a public application. File.Storage enforces the per-file
/// <c>CandidateDocument</c> profile (5 MB; pdf/jpg/jpeg/png) again on its side.
/// </summary>
public sealed class RecruitmentCandidateDocumentsOptions
{
    /// <summary><c>Recruitment:CandidateDocuments:MaxFiles</c> — files per application post (default 5).</summary>
    public int MaxFiles { get; set; } = 5;

    /// <summary><c>Recruitment:CandidateDocuments:MaxBytes</c> — bytes per file (default 5 MB = 5,242,880).</summary>
    public long MaxBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// <c>Recruitment:CandidateDocuments:MaxTotalBytes</c> — all files of one application post (default 9 MiB =
    /// 9,437,184): Production Kestrel at the gateway and HR.Core refuses request bodies over 10 MB.
    /// </summary>
    public long MaxTotalBytes { get; set; } = 9 * 1024 * 1024;
}

/// <summary>HCM-030 (D3): settings of <see cref="RecruitmentEligibilityRules"/>.</summary>
public sealed class RecruitmentEligibilityOptions
{
    /// <summary>
    /// <c>Recruitment:Eligibility:QualificationRanks</c> — MDM qualification code → rank (higher = more advanced).
    /// The MDM catalog has no order (and its seed pack digest is pinned), so the order lives here. Configured entries
    /// are added to (or override) the defaults DIP=1, BACH=2, MASTER=3, PHD=4; an unranked code is flagged
    /// <c>QualificationUnranked</c>.
    /// </summary>
    public Dictionary<string, int> QualificationRanks { get; set; } =
        new(RecruitmentEligibilityRules.DefaultQualificationRanks, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>Recruitment:Eligibility:AgeBoundsByRegime</c> — optional default age bounds per service regime id (law50,
    /// law53, …), used when the requisition sets none. Empty by default: the repository holds no legal age data, so no
    /// Production values are shipped.
    /// </summary>
    public Dictionary<string, RecruitmentAgeBounds> AgeBoundsByRegime { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>HCM-030 (D3): an age range (inclusive); null = not checked.</summary>
public sealed class RecruitmentAgeBounds
{
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
}

/// <summary>HCM-026: recruitment background job settings.</summary>
public sealed class RecruitmentJobsOptions
{
    /// <summary><c>Recruitment:Jobs:IntakeCloseEnabled</c> — runs <c>RecruitmentIntakeCloseJob</c> (default true).</summary>
    public bool IntakeCloseEnabled { get; set; } = true;

    /// <summary><c>Recruitment:Jobs:IntakeCloseIntervalMinutes</c> (default 60).</summary>
    public int IntakeCloseIntervalMinutes { get; set; } = 60;

    /// <summary>HCM-027: <c>Recruitment:Jobs:OfferExpiryEnabled</c> — runs <c>RecruitmentOfferExpiryJob</c> (default true).</summary>
    public bool OfferExpiryEnabled { get; set; } = true;

    /// <summary>HCM-027: <c>Recruitment:Jobs:OfferExpiryIntervalMinutes</c> (default 60).</summary>
    public int OfferExpiryIntervalMinutes { get; set; } = 60;

    /// <summary>HCM-027: <c>Recruitment:Jobs:ReserveExpiryEnabled</c> — runs <c>RecruitmentReserveExpiryJob</c> (default true).</summary>
    public bool ReserveExpiryEnabled { get; set; } = true;

    /// <summary>HCM-027: <c>Recruitment:Jobs:ReserveExpiryIntervalMinutes</c> (default 1440, daily).</summary>
    public int ReserveExpiryIntervalMinutes { get; set; } = 1440;

    /// <summary>
    /// COM-030 (PLAN-30 F4): <c>Recruitment:Jobs:CandidateNotificationsEnabled</c> — runs <c>CandidateNotificationJob</c>
    /// (offer reminders, planning and sending of candidate messages; default true).
    /// </summary>
    public bool CandidateNotificationsEnabled { get; set; } = true;

    /// <summary>COM-030: <c>Recruitment:Jobs:CandidateNotificationsIntervalSeconds</c> (default 60, at least 10).</summary>
    public int CandidateNotificationsIntervalSeconds { get; set; } = 60;
}
