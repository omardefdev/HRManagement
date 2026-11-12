using HRManagement.ServiceDefaults;

namespace Admin.Logistics.Infrastructure.OrgScope;

/// <summary>
/// PLAN-52 (§1, P-4): the scope keys of admin-logistics. Program.cs passes it to <c>AddOrgScopeFilter</c>; the architecture tests and the translation
/// tests call it for real.
/// </summary>
public static class AdminLogisticsOrgScopeKeys
{
    public const string Service = "admin-logistics";

    public static void Configure(OrgScopeKeyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}
