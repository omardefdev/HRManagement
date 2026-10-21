namespace Payroll.Application.Dtos;

/// <summary>AC-23: freeze provenance for auditor review.</summary>
public record PayrollRunFreezeStatusDto(
    Guid PayrollRunId,
    bool ResultsFrozen,
    bool ValidatedAtCalculate,
    string? ConfigContentHash);
