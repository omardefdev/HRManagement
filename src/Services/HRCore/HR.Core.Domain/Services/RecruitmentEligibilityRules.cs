namespace HR.Core.Domain.Services;

/// <summary>HCM-030 (D3): the applicant data the eligibility check reads (snapshot on the recruitment application).</summary>
public sealed record EligibilityProfile(string? EducationLevelCode, decimal? YearsOfExperience, DateOnly? BirthDate);

/// <summary>HCM-030 (D2): requisition criteria; a null criterion is not checked.</summary>
public sealed record EligibilityCriteria(string? MinQualificationCode, decimal? MinYearsOfExperience, int? MinAge, int? MaxAge)
{
    public bool IsEmpty => MinQualificationCode is null && MinYearsOfExperience is null && MinAge is null && MaxAge is null;
}

/// <summary>
/// HCM-030 (D3, G-17): automatic eligibility check at intake. It only flags — it never rejects: HR sees the flags while
/// screening, and moving a flagged application to «القائمة المختصرة» needs a recorded reason
/// (<see cref="MinOverrideReasonLength"/>). Qualification order comes from configuration
/// (<c>Recruitment:Eligibility:QualificationRanks</c>) because the MDM qualification catalog has no order; the
/// repository holds no legal age data, so age bounds come from the requisition (or optional configuration).
/// </summary>
public static class RecruitmentEligibilityRules
{
    public const string BelowMinQualification = "BelowMinQualification";
    public const string QualificationUnranked = "QualificationUnranked";
    public const string BelowMinExperience = "BelowMinExperience";
    public const string AgeBelowMin = "AgeBelowMin";
    public const string AgeAboveMax = "AgeAboveMax";
    public const string MissingProfileData = "MissingProfileData";

    public const int MinOverrideReasonLength = 10;
    public const string OverrideAuditAction = "EligibilityOverride";
    public const string OverrideReasonRequiredMessage =
        "للطلب ملاحظات أهلية — الإدراج في القائمة المختصرة يتطلب سبب تجاوز لا يقل عن 10 أحرف.";

    /// <summary>Default qualification ranks (higher = more advanced) for the national MDM codes.</summary>
    public static readonly IReadOnlyDictionary<string, int> DefaultQualificationRanks =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["DIP"] = 1, ["BACH"] = 2, ["MASTER"] = 3, ["PHD"] = 4 };

    /// <summary>Flag codes in display order, with their Arabic labels.</summary>
    public static readonly IReadOnlyList<(string Code, string LabelAr)> Flags =
    [
        (BelowMinQualification, "دون المؤهل المطلوب"),
        (QualificationUnranked, "تعذّر تقييم المؤهل"),
        (BelowMinExperience, "خبرة أقل من المطلوب"),
        (AgeBelowMin, "دون الحد الأدنى للعمر"),
        (AgeAboveMax, "فوق الحد الأعلى للعمر"),
        (MissingProfileData, "بيانات ناقصة")
    ];

    public static IReadOnlyList<string> Evaluate(
        EligibilityProfile profile,
        EligibilityCriteria criteria,
        DateOnly applicationDate,
        IReadOnlyDictionary<string, int>? qualificationRanks = null)
    {
        var ranks = qualificationRanks is { Count: > 0 } ? qualificationRanks : DefaultQualificationRanks;
        var flags = new HashSet<string>();

        if (!string.IsNullOrWhiteSpace(criteria.MinQualificationCode))
        {
            if (string.IsNullOrWhiteSpace(profile.EducationLevelCode))
                flags.Add(MissingProfileData);
            else if (Rank(ranks, criteria.MinQualificationCode) is not int min
                     || Rank(ranks, profile.EducationLevelCode) is not int held)
                flags.Add(QualificationUnranked);
            else if (held < min)
                flags.Add(BelowMinQualification);
        }

        if (criteria.MinYearsOfExperience is decimal minYears)
        {
            if (profile.YearsOfExperience is not decimal years)
                flags.Add(MissingProfileData);
            else if (years < minYears)
                flags.Add(BelowMinExperience);
        }

        if (criteria.MinAge is not null || criteria.MaxAge is not null)
        {
            if (profile.BirthDate is not DateOnly birthDate)
                flags.Add(MissingProfileData);
            else
            {
                var age = AgeOn(birthDate, applicationDate);
                if (criteria.MinAge is int minAge && age < minAge)
                    flags.Add(AgeBelowMin);
                if (criteria.MaxAge is int maxAge && age > maxAge)
                    flags.Add(AgeAboveMax);
            }
        }

        return Flags.Select(f => f.Code).Where(flags.Contains).ToList();
    }

    /// <summary>Completed years on <paramref name="on"/> (a birthday counts on the day itself).</summary>
    public static int AgeOn(DateOnly birthDate, DateOnly on)
    {
        var age = on.Year - birthDate.Year;
        if (on < birthDate.AddYears(age))
            age--;
        return age;
    }

    /// <summary>Highest-ranked code among <paramref name="codes"/> (null when none is ranked).</summary>
    public static string? HighestRanked(IEnumerable<string?> codes, IReadOnlyDictionary<string, int>? qualificationRanks = null)
    {
        var ranks = qualificationRanks is { Count: > 0 } ? qualificationRanks : DefaultQualificationRanks;
        return codes
            .Where(c => !string.IsNullOrWhiteSpace(c) && Rank(ranks, c!) is not null)
            .OrderByDescending(c => Rank(ranks, c!))
            .FirstOrDefault()?.Trim();
    }

    /// <summary>Stored form of <see cref="Evaluate"/>: comma-separated codes, null when there is no flag.</summary>
    public static string? Format(IReadOnlyCollection<string> flags) => flags.Count == 0 ? null : string.Join(',', flags);

    public static IReadOnlyList<string> Parse(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string LabelAr(string code) =>
        Flags.FirstOrDefault(f => f.Code == code).LabelAr ?? code;

    public static bool IsValidOverrideReason(string? reason) =>
        reason is not null && reason.Trim().Length >= MinOverrideReasonLength;

    private static int? Rank(IReadOnlyDictionary<string, int> ranks, string code) =>
        ranks.TryGetValue(code.Trim(), out var rank) ? rank : null;
}
