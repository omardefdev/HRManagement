namespace Payroll.Application.Services;

/// <summary>FRD-001 salary-line input for continuous anti-fraud scanning.</summary>
public sealed record SalaryLineScanInput(
    Guid EmployeeId,
    decimal NetPay,
    string? Iban = null,
    bool? HasAttendance = null,
    bool? IsDeceased = null,
    DateTime? HireDate = null,
    bool? HasSourceRecord = null,
    string? NationalIdHash = null,
    decimal? ExpectedNetPay = null,
    int? ApproverActionCount = null);
