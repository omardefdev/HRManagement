using HRManagement.ServiceDefaults;

namespace HR.Core.Infrastructure.OrgScope;

/// <summary>
/// PLAN-52 (§1, P-4): the scope keys of hr-core. Program.cs passes it to <c>AddOrgScopeFilter</c>; the architecture tests and the translation
/// tests call it for real.
/// </summary>
public static class HrCoreOrgScopeKeys
{
    public const string Service = "hr-core";

    public static void Configure(OrgScopeKeyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}
