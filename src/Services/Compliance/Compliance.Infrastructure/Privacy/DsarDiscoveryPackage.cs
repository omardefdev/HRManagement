namespace Compliance.Infrastructure.Privacy;

/// <summary>AC-27: masked DSAR discovery package (PRV-002 / GOV-001).</summary>
public sealed record DsarDiscoveryPackage(
    int Version,
    string Card,
    Guid TenantId,
    Guid? SubjectEmployeeId,
    string? SubjectNationalIdHash,
    Guid? SubjectCitizenApplicationId,
    DateTime ExportedAtUtc,
    Guid ExportedByUserId,
    Guid? RequesterUserId,
    string PackageContentHash,
    IReadOnlyList<DsarDiscoverySourceSection> Sources,
    IReadOnlyList<DsarDiscoveryField> Fields);

public sealed record DsarDiscoverySourceSection(
    string Service,
    bool Collected,
    string? Error);

public sealed record DsarDiscoveryField(
    string Source,
    string Name,
    object? Value);
