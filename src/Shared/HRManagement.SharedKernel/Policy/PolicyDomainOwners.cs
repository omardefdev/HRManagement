namespace HRManagement.SharedKernel.Policy;

/// <summary>
/// PLAN-42 (G-POL-08): which service client may ask for a decision in a domain, and on which subject types. <c>Core.*</c> has
/// no entry: those domains run inside HR.Core and are never reached over HTTP. PLAN-49's domain catalog replaces this table.
/// </summary>
public static class PolicyDomainOwners
{
    public sealed record Owner(string ClientId, IReadOnlySet<string> SubjectTypes);

    private static Owner For(string clientId, params string[] subjectTypes) =>
        new(clientId, new HashSet<string>(subjectTypes, StringComparer.Ordinal));

    private static readonly IReadOnlyDictionary<string, Owner> Owners = new Dictionary<string, Owner>(StringComparer.Ordinal)
    {
        [PolicyDomains.AssetsCustody] = For("svc-admin-logistics", "Asset", "AssetAssignment"),
        [PolicyDomains.AssetsProcurement] = For("svc-admin-logistics", "SupplyOrder"),
        [PolicyDomains.IDCardLifecycle] = For("svc-idcard", "CardRequest"),
        [PolicyDomains.ContractsLeave] = For("svc-hr-contracts", "LeaveRequest"),
        [PolicyDomains.ContractsAdvance] = For("svc-hr-contracts", "AdvanceRequest"),
        [PolicyDomains.ContractsHire] = For("svc-hr-contracts", "HireWizard"),
        [PolicyDomains.ContractsSeparation] = For("svc-hr-contracts", "Separation"),
        [PolicyDomains.ContractsContract] = For("svc-hr-contracts", "Contract"),
        [PolicyDomains.MovementsTransfer] = For("svc-hr-movements", "Transfer"),
        [PolicyDomains.MovementsWorkforce] = For("svc-hr-movements", "WorkforceGap"),
        [PolicyDomains.HealthInsurance] = For("svc-hr-health", "InsuranceCard"),
        [PolicyDomains.ComplianceDisciplinary] = For("svc-compliance", "DisciplinaryAction"),
        [PolicyDomains.PayrollPeriod] = For("svc-payroll", "PayrollPeriod"),
        [PolicyDomains.ITTickets] = For("svc-it-infra", "Ticket"),
        [PolicyDomains.ArchiveRetention] = For("svc-records-archive", "ArchiveRecord"),
        [PolicyDomains.TrainingLearning] = For("svc-hr-training", "TrainingCourse"),
        [PolicyDomains.AnalyticsEtl] = For("svc-dev-analytics", "EtlJob")
    };

    public static IReadOnlyCollection<string> OwnedDomains => (IReadOnlyCollection<string>)Owners.Keys;

    /// <summary>True when the client owns at least one domain.</summary>
    public static bool IsOwnerOfAnyDomain(string? clientId) =>
        !string.IsNullOrWhiteSpace(clientId)
        && Owners.Values.Any(o => string.Equals(o.ClientId, clientId, StringComparison.Ordinal));

    public static bool Owns(string? clientId, string? domain) =>
        !string.IsNullOrWhiteSpace(clientId)
        && domain is not null
        && Owners.TryGetValue(domain, out var owner)
        && string.Equals(owner.ClientId, clientId, StringComparison.Ordinal);

    public static bool AllowsSubjectType(string? domain, string? subjectType) =>
        domain is not null
        && subjectType is not null
        && Owners.TryGetValue(domain, out var owner)
        && owner.SubjectTypes.Contains(subjectType);
}
