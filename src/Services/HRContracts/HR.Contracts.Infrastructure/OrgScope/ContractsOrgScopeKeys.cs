using HRManagement.ServiceDefaults;

namespace HR.Contracts.Infrastructure.OrgScope;

/// <summary>
/// PLAN-52 (§1, P-4): the scope keys of hr-contracts. Program.cs passes it to <c>AddOrgScopeFilter</c>; the architecture tests and the translation
/// tests call it for real.
/// </summary>
public static class ContractsOrgScopeKeys
{
    public const string Service = "hr-contracts";

    public static void Configure(OrgScopeKeyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}
