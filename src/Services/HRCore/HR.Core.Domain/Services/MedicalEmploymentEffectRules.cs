using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Services;

/// <summary>
/// Maps medical recommendations/decisions (DFD P2.3 → P1.1) to employment status transitions.
/// </summary>
public static class MedicalEmploymentEffectRules
{
    public static EmploymentStatus? ResolveStatusFromRecommendation(string recommendationType) =>
        recommendationType switch
        {
            "Retirement" => EmploymentStatus.Retired,
            "FitnessForDuty" => EmploymentStatus.Active,
            "LimitedDuties" => null,
            "Compensation" => null,
            "Other" => null,
            _ => null
        };

    public static EmploymentStatus? ResolveStatusFromMedicalRef(string medicalRef) =>
        medicalRef switch
        {
            "MEDICAL_RETIREMENT" => EmploymentStatus.Retired,
            "RETURN_TO_WORK" => EmploymentStatus.Active,
            "LIMITED_DUTIES" => null,
            _ => null
        };

    public static bool RequiresLimitedDutiesRecord(string recommendationType) =>
        recommendationType == "LimitedDuties";

    public static bool RequiresLimitedDutiesRecordFromRef(string medicalRef) =>
        medicalRef == "LIMITED_DUTIES";

    public static string BuildHistoryMarker(string prefix, Guid referenceId) =>
        $"{prefix}:{referenceId}";

    public static string BuildReason(string marker, string detail, string? sourceSystem) =>
        string.IsNullOrWhiteSpace(sourceSystem)
            ? $"{marker} — {detail}"
            : $"{marker} Source={sourceSystem} — {detail}";
}
