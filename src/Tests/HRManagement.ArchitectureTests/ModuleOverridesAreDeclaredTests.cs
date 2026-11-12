using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-51 (§4 rule 3, §6, P-6): <c>[ModulePolicyOverride]</c> names a registered policy, and never authorizes a write by itself. A write
/// under an override (its own or its class's) also names its own policy on the action or the class; an override placed on a write action
/// itself is allowed only with an approval policy (PLAN-50's approvals).
/// </summary>
public sealed class ModuleOverridesAreDeclaredTests
{
    /// <summary>The approval policies an override on a write action may name.</summary>
    public static readonly IReadOnlySet<string> ApprovalPolicies = new HashSet<string>(StringComparer.Ordinal)
    {
        HrManagementPolicies.HrMovementsApprovalAccess,
        HrManagementPolicies.HrManagementApprove,
        HrManagementPolicies.HrCoreApprove,
        HrManagementPolicies.HrContractsApprove,
        HrManagementPolicies.PolicyApprover,
        HrManagementPolicies.WorkflowStepAction,
    };

    public static IReadOnlyList<string> Violations(IEnumerable<ActionInfo> actions)
    {
        var violations = new List<string>();
        foreach (var action in actions.Where(a => a.IsWrite && a.EffectiveOverride is not null))
        {
            var name = $"{action.Service}:{action.Key}";
            if (!action.HasExplicitPolicy)
                violations.Add($"{name}: a write under [ModulePolicyOverride({action.EffectiveOverride})] names no policy of its own");
            if (action.ActionOverride is { } own && !ApprovalPolicies.Contains(own))
                violations.Add($"{name}: [ModulePolicyOverride({own})] on a write action, and {own} is not an approval policy");
        }

        return violations;
    }

    [Fact]
    public void No_override_authorizes_a_write_by_itself()
    {
        var violations = Violations(ActionCatalog.Actions);
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public async Task Every_override_names_a_registered_policy()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        await using var provider = services.BuildServiceProvider();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var named = ActionCatalog.Actions
            .SelectMany(a => new[] { a.ActionOverride, a.ClassOverride })
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(named);

        var unknown = new List<string>();
        foreach (var policy in named)
        {
            if (await policies.GetPolicyAsync(policy) is null)
                unknown.Add(policy);
        }

        Assert.True(unknown.Count == 0, "Overrides naming a policy that is not registered: " + string.Join(", ", unknown));
    }
}
