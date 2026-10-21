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

    /// <summary>Background jobs settings (<c>Recruitment:Jobs:*</c>).</summary>
    public RecruitmentJobsOptions Jobs { get; set; } = new();
}

/// <summary>HCM-026: recruitment background job settings.</summary>
public sealed class RecruitmentJobsOptions
{
    /// <summary><c>Recruitment:Jobs:IntakeCloseEnabled</c> — runs <c>RecruitmentIntakeCloseJob</c> (default true).</summary>
    public bool IntakeCloseEnabled { get; set; } = true;

    /// <summary><c>Recruitment:Jobs:IntakeCloseIntervalMinutes</c> (default 60).</summary>
    public int IntakeCloseIntervalMinutes { get; set; } = 60;
}
