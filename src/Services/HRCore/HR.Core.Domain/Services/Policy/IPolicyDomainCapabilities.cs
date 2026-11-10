using HRManagement.SharedKernel.Policy;

namespace HR.Core.Domain.Services.Policy;

/// <summary>
/// PLAN-47: what a policy domain may do. PLAN-49: answered from the domain catalog (<see cref="PolicyDomainCatalog"/>) by
/// <c>CatalogPolicyDomainCapabilities</c>; the allow-all default of PLAN-47 is gone, so nothing validates without the catalog.
/// The validator (<see cref="PolicyRuleValidator"/>) is the only place that refuses.
/// </summary>
public interface IPolicyDomainCapabilities
{
    /// <summary>Whether the engine governs the domain. A rule, a draft or a publication in any other domain is refused.</summary>
    bool IsGoverned(string domain);

    /// <summary>Whether a call site of the domain consumes the effect.</summary>
    bool AllowsEffect(string domain, PolicyEffect effect);

    /// <summary>Whether a SetField effect may write the field.</summary>
    bool AllowsSetField(string domain, string field);

    /// <summary>Every action the call sites of the domain send, whatever the entity type.</summary>
    IReadOnlySet<string> Actions(string domain);

    /// <summary>The entity types that have the action.</summary>
    IReadOnlyCollection<string> SubjectTypesOf(string domain, string action);

    /// <summary>Whether a rule may name the workflow code for the entity type (a TriggerWorkflow effect).</summary>
    bool AllowsWorkflowCode(string domain, string subjectType, string workflowCode);
}
