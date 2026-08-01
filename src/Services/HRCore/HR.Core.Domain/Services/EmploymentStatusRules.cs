using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Services;

public static class EmploymentStatusRules
{
    private static readonly Dictionary<EmploymentStatus, HashSet<EmploymentStatus>> AllowedTransitions = new()
    {
        [EmploymentStatus.Active] = new()
        {
            EmploymentStatus.OnLeave,
            EmploymentStatus.Suspended,
            EmploymentStatus.Terminated,
            EmploymentStatus.Retired,
            EmploymentStatus.Resigned,
            EmploymentStatus.Seconded,
            EmploymentStatus.Deceased,
            EmploymentStatus.Merged
        },
        [EmploymentStatus.OnLeave] = new()
        {
            EmploymentStatus.Active,
            EmploymentStatus.Terminated,
            EmploymentStatus.Resigned,
            EmploymentStatus.Retired,
            EmploymentStatus.Deceased
        },
        [EmploymentStatus.Suspended] = new()
        {
            EmploymentStatus.Active,
            EmploymentStatus.Terminated,
            EmploymentStatus.Resigned,
            EmploymentStatus.Deceased
        },
        [EmploymentStatus.Seconded] = new()
        {
            EmploymentStatus.Active,
            EmploymentStatus.Terminated,
            EmploymentStatus.Resigned,
            EmploymentStatus.Deceased
        },
        [EmploymentStatus.Terminated] = new() { EmploymentStatus.Anonymized },
        [EmploymentStatus.Retired] = new() { EmploymentStatus.Anonymized },
        [EmploymentStatus.Resigned] = new() { EmploymentStatus.Anonymized },
        [EmploymentStatus.Deceased] = new() { EmploymentStatus.Anonymized },
        [EmploymentStatus.Merged] = new(),
        [EmploymentStatus.Anonymized] = new(),
    };

    public static bool CanTransition(EmploymentStatus from, EmploymentStatus to)
    {
        if (from == to) return false;
        return AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>Terminated / Resigned / Retired — UI and reports may treat as Separated umbrella.</summary>
    public static bool IsSeparatedFamily(EmploymentStatus status) =>
        status is EmploymentStatus.Terminated
            or EmploymentStatus.Resigned
            or EmploymentStatus.Retired;

    public static bool IsTerminal(EmploymentStatus status) =>
        status is EmploymentStatus.Terminated
            or EmploymentStatus.Retired
            or EmploymentStatus.Resigned
            or EmploymentStatus.Deceased
            or EmploymentStatus.Merged
            or EmploymentStatus.Anonymized;

    public static EmploymentStatus MapSeparationType(string? separationType)
    {
        if (string.IsNullOrWhiteSpace(separationType))
            return EmploymentStatus.Terminated;

        return separationType.Trim().ToLowerInvariant() switch
        {
            "resignation" => EmploymentStatus.Resigned,
            "retired" or "retirement" => EmploymentStatus.Retired,
            "deceased" or "death" => EmploymentStatus.Deceased,
            _ => EmploymentStatus.Terminated
        };
    }
}
