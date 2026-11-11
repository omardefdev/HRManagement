using HR.Core.Domain.Services.Policy;
using HRManagement.SharedKernel.Policy;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>
/// PLAN-49 (G-POL-02): PLAN-47's capabilities, answered from <see cref="PolicyDomainCatalog"/> and from nothing else. A domain
/// the catalog does not govern has no capability at all.
/// </summary>
public sealed class CatalogPolicyDomainCapabilities : IPolicyDomainCapabilities
{
    public static readonly CatalogPolicyDomainCapabilities Instance = new();

    private static readonly IReadOnlySet<string> NoActions = new HashSet<string>(StringComparer.Ordinal);

    private static PolicyDomainDescriptor? Governed(string domain) =>
        PolicyDomainCatalog.Find(domain) is { IsGoverned: true } descriptor ? descriptor : null;

    public bool IsGoverned(string domain) => Governed(domain) is not null;

    public bool AllowsEffect(string domain, PolicyEffect effect) => Governed(domain)?.AllowedEffects.Contains(effect) == true;

    public bool AllowsSetField(string domain, string field) => Governed(domain)?.SetFields.Contains(field) == true;

    public IReadOnlySet<string> Actions(string domain) => Governed(domain)?.Actions ?? NoActions;

    public IReadOnlyCollection<string> SubjectTypesOf(string domain, string action) =>
        Governed(domain)?.SubjectTypesOf(action).ToList() ?? [];

    public bool AllowsWorkflowCode(string domain, string subjectType, string workflowCode) =>
        Governed(domain)?.AllowsWorkflowCode(subjectType, workflowCode) == true;

    public bool AllowsWorkflowStart(string domain, string subjectType, string action, string workflowCode) =>
        Governed(domain)?.AllowsWorkflowStart(subjectType, action, workflowCode) == true;
}
