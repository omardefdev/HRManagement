namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-001: legislation source with key articles and instrument links.</summary>
public sealed record LegislationSourceDetailDto(
    string LegalSourceId,
    string InstrumentType,
    string Number,
    string TitleAr,
    string? TitleEn,
    DateOnly IssueDate,
    DateOnly EffectiveDate,
    DateOnly? PublicationDate,
    string Status,
    string? Scope,
    string Owner,
    string? CertifiedTextHash,
    string RegisterVersion,
    IReadOnlyList<LegislationKeyArticleDto> KeyArticles,
    IReadOnlyList<LegislationInstrumentLinkDto> AmendedBy,
    IReadOnlyList<LegislationInstrumentLinkDto> Repeals)
{
    public string DisplayLabel => $"{Number} — {TitleAr}";
}
