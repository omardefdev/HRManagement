namespace HRManagement.SharedKernel.Enums;

/// <summary>
/// PLAN-60 (G-SEP-11): the employment statuses that end a person's service, for services that cannot reference HR.Core's domain
/// (HR.Contracts reads the status from HR.Core's lifecycle flags as a name). A unit test keeps this set equal to HR.Core's
/// <c>EmploymentStatusRules.IsTerminal</c>.
/// </summary>
public static class EmploymentStatusTerminal
{
    public static IReadOnlySet<EmploymentStatus> Statuses { get; } = new HashSet<EmploymentStatus>
    {
        EmploymentStatus.Terminated,
        EmploymentStatus.Retired,
        EmploymentStatus.Resigned,
        EmploymentStatus.Deceased,
        EmploymentStatus.Merged,
        EmploymentStatus.Anonymized
    };

    public static bool IsTerminal(EmploymentStatus status) => Statuses.Contains(status);

    /// <summary>Whether <paramref name="name"/> (as HR.Core writes <c>CurrentStatus.ToString()</c>) names a terminal status.</summary>
    public static bool IsTerminalName(string? name, out EmploymentStatus status) =>
        Enum.TryParse(name, ignoreCase: true, out status) && Enum.IsDefined(status) && IsTerminal(status);
}
