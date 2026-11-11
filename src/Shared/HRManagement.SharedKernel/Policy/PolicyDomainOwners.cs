namespace HRManagement.SharedKernel.Policy;

/// <summary>
/// PLAN-42 (G-POL-08): which service client may ask HR.Core for a decision in a domain, and on which subject types and actions.
/// PLAN-49: read from <see cref="PolicyDomainCatalog"/>, never held here. Only a governed domain has an owner; <c>Core.*</c>
/// has none (those domains run inside HR.Core and are never reached over HTTP), and an out-of-scope domain has none either.
/// </summary>
public static class PolicyDomainOwners
{
    /// <summary>The governed domains a service client owns.</summary>
    public static IReadOnlyCollection<string> OwnedDomains =>
        PolicyDomainCatalog.Governed.Where(d => d.OwnerClientId is not null).Select(d => d.Domain).ToList();

    /// <summary>True when the client owns at least one governed domain.</summary>
    public static bool IsOwnerOfAnyDomain(string? clientId) =>
        !string.IsNullOrWhiteSpace(clientId)
        && PolicyDomainCatalog.Governed.Any(d => string.Equals(d.OwnerClientId, clientId, StringComparison.Ordinal));

    public static bool Owns(string? clientId, string? domain) =>
        !string.IsNullOrWhiteSpace(clientId)
        && PolicyDomainCatalog.Find(domain) is { IsGoverned: true, OwnerClientId: { } owner }
        && string.Equals(owner, clientId, StringComparison.Ordinal);

    public static bool AllowsSubjectType(string? domain, string? subjectType) =>
        PolicyDomainCatalog.Find(domain) is { IsGoverned: true } descriptor && descriptor.AllowsSubjectType(subjectType);

    /// <summary>PLAN-49: the action must be one the catalog lists for the subject type.</summary>
    public static bool AllowsAction(string? domain, string? subjectType, string? action) =>
        PolicyDomainCatalog.Find(domain) is { IsGoverned: true } descriptor && descriptor.AllowsAction(subjectType, action);
}
