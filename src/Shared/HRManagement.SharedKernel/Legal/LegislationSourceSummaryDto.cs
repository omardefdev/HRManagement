namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-001: read model for legislation register list/dropdown.</summary>
public sealed record LegislationSourceSummaryDto(
    string LegalSourceId,
    string InstrumentType,
    string Number,
    string TitleAr,
    string? TitleEn,
    DateOnly EffectiveDate,
    string Status)
{
    public string DisplayLabel => $"{Number} — {TitleAr}";
}
