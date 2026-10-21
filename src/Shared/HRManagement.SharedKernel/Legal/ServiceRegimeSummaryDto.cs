namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-003: read-only summary of a service regime.</summary>
public sealed record ServiceRegimeSummaryDto(
    string LegalRegimeId,
    string RegimeName,
    RegimeType Type,
    string LegislationRef,
    string? ResolvedLegalSourceId,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive)
{
    public string DisplayLabel => RegimeName;
}
