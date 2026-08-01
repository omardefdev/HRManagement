namespace HRManagement.Integration.Abstractions.Tax;

public sealed record TaxWithholdingReportRequest(
    Guid PayrollRunId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<TaxWithholdingLine> Lines);

public sealed record TaxWithholdingLine(
    Guid EmployeeId,
    string? NationalId,
    decimal TaxableAmount,
    decimal WithheldAmount);
