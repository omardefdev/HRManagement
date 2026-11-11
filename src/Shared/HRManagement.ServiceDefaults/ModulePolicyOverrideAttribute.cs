using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-50 (§1): on an action or a controller, <see cref="HrModuleAuthorizationFilter"/> evaluates this policy instead of the
/// module's. It widens nothing by itself: it swaps one named policy for another, and the action's own <c>[Authorize]</c> still applies
/// on top. The action's attribute wins over the controller's. A name that is not registered fails the boot
/// (<see cref="ModulePolicyOverrideValidator"/>). In PLAN-50 it never sits on a create, edit, execute or cancel action.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ModulePolicyOverrideAttribute(string policy) : Attribute
{
    public string Policy { get; } = policy;

    /// <summary>The attribute that applies to an action: the action's own, else its controller's (endpoint metadata lists the controller first).</summary>
    public static ModulePolicyOverrideAttribute? For(ActionDescriptor action) =>
        action.EndpointMetadata.OfType<ModulePolicyOverrideAttribute>().LastOrDefault();
}

/// <summary>PLAN-50 (§1, rule 1): every policy an override names must be registered, or the host does not start.</summary>
public sealed class ModulePolicyOverrideValidator(
    IActionDescriptorCollectionProvider actions,
    IAuthorizationPolicyProvider policies) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var unknown = await FindUnknownPoliciesAsync(actions.ActionDescriptors.Items, policies);
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                "ModulePolicyOverride names a policy that is not registered: " + string.Join(", ", unknown));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The policy names of every override on these actions that the provider does not know, in order and without repeats.</summary>
    public static async Task<IReadOnlyList<string>> FindUnknownPoliciesAsync(
        IEnumerable<ActionDescriptor> actionDescriptors,
        IAuthorizationPolicyProvider policyProvider)
    {
        var unknown = new List<string>();
        foreach (var name in actionDescriptors
                     .SelectMany(a => a.EndpointMetadata.OfType<ModulePolicyOverrideAttribute>())
                     .Select(a => a.Policy)
                     .Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(name) || await policyProvider.GetPolicyAsync(name) is null)
                unknown.Add(name);
        }

        return unknown;
    }
}
