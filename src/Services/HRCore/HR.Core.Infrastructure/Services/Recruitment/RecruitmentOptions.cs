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

    /// <summary>Background jobs settings (<c>Recruitment:Jobs:*</c>). PLAN-26 session 2 adds the intake-close job here.</summary>
    public RecruitmentJobsOptions Jobs { get; set; } = new();
}

/// <summary>HCM-026: recruitment background job settings — extended in PLAN-26 session 2.</summary>
public sealed class RecruitmentJobsOptions
{
}
