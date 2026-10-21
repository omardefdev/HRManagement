using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>HCM-026: default assessment criteria written by the acceptance and demo seeders (new JSON format).</summary>
internal static class RecruitmentSeedCriteria
{
    public const string InterviewKey = "interview";
    public const string ExperienceKey = "experience";

    public static readonly IReadOnlyList<AssessmentCriteriaItem> Items =
    [
        new(InterviewKey, "المقابلة", 40m, 100m, AssessmentStage.Interview),
        new(ExperienceKey, "الخبرة", 60m, 100m, AssessmentStage.Experience)
    ];

    public static string Json => AssessmentCriteriaSerializer.Serialize(Items);
}
