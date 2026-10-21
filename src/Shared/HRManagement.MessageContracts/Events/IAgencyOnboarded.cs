namespace HRManagement.MessageContracts.Events;

/// <summary>ONB-001: agency manifest applied — tenant + org tree ready for MDM/policy overlays.</summary>
public interface IAgencyOnboarded
{
    Guid TenantId { get; }
    string AgencyId { get; }
    string PackId { get; }
    string ReleaseDigest { get; }
    IReadOnlyList<string> OrgUnitCodes { get; }
    DateTime OnboardedAt { get; }
    string? CorrelationId { get; }

    int SchemaVersion => 1;
}
