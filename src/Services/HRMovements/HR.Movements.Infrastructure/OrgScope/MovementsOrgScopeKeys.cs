using HRManagement.ServiceDefaults;

namespace HR.Movements.Infrastructure.OrgScope;

/// <summary>
/// PLAN-52 (§1, P-4): the scope keys of hr-movements. Program.cs passes it to <c>AddOrgScopeFilter</c>; the architecture tests and the translation
/// tests call it for real.
/// </summary>
public static class MovementsOrgScopeKeys
{
    public const string Service = "hr-movements";

    public static void Configure(OrgScopeKeyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}
