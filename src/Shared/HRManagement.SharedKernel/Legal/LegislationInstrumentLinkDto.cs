namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-001: amended-by / repeals link between legislation sources.</summary>
public sealed record LegislationInstrumentLinkDto(
    string FromLegalSourceId,
    string ToLegalSourceId,
    LegislationInstrumentLinkType LinkType,
    string? ToNumber,
    string? ToTitleAr);
