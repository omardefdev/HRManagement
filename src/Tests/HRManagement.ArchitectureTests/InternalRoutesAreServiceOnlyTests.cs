using System.Reflection;
using HRManagement.ServiceDefaults;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-42 §8, moved here by PLAN-51 (§2, §6): <c>api/internal/*</c> is exempt from the gateway proof, so it serves service accounts alone.
/// Every action of every web assembly whose route starts with <c>api/internal</c> carries a <see cref="ServicePolicies"/> policy (on the
/// action or its class) and is never anonymous. One rule, one test: it replaces <c>S2s042InternalRoutesAreServiceOnlyTests</c>.
/// </summary>
public sealed class InternalRoutesAreServiceOnlyTests
{
    private const string Prefix = "api/internal";

    public static readonly IReadOnlySet<string> ServicePolicyNames = typeof(ServicePolicies)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f is { IsLiteral: true })
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsInternal(ActionInfo action) =>
        action.RouteTemplates.Any(t => t.TrimStart('/').StartsWith(Prefix, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Violations(IEnumerable<ActionInfo> actions)
    {
        var violations = new List<string>();
        foreach (var action in actions.Where(IsInternal))
        {
            var name = $"{action.Service}:{action.Controller.FullName}.{action.Method.Name}";
            if (action.Anonymous)
                violations.Add($"{name}: anonymous on an internal route");
            else if (!action.AllPolicies.Any(ServicePolicyNames.Contains))
                violations.Add($"{name}: no ServicePolicies policy ({string.Join(", ", action.AllPolicies.DefaultIfEmpty("<none>"))})");
        }

        return violations;
    }

    [Fact]
    public void Every_internal_route_of_every_web_assembly_is_for_services_only()
    {
        Assert.True(ActionCatalog.Actions.Count > 1000, $"only {ActionCatalog.Actions.Count} actions were read: the guard is not looking");
        var violations = Violations(ActionCatalog.Actions);
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void The_guard_sees_the_internal_controllers_it_is_meant_to_see()
    {
        var names = ActionCatalog.Actions.Where(IsInternal).Select(a => a.Controller.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
                 {
                     "PolicyEnforceInternalController", "CodeGenerationInternalController", "EmployeeLifecycleInternalController",
                     "ServiceRegimeInternalController", "DsarInternalController", "PayGradeLookupInternalController", "DirectoryInternalSyncController",
                     // PLAN-50: the active approval delegations of a user.
                     "ApprovalDelegationsInternalController"
                 })
            Assert.Contains(expected, names);
    }
}
