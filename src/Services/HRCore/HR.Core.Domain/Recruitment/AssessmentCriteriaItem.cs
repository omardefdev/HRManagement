using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Recruitment;

/// <summary>HCM-026: one weighted item inside a versioned assessment criteria set.</summary>
public sealed record AssessmentCriteriaItem(
    string Key,
    string NameAr,
    decimal Weight,
    decimal MaxScore,
    AssessmentStage Stage,
    bool IsMandatory = true,
    string? Description = null);
