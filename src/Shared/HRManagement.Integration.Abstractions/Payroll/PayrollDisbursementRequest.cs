namespace HRManagement.Integration.Abstractions.Payroll;

public sealed record PayrollDisbursementRequest(
    Guid PayrollRunId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string CurrencyCode,
    IReadOnlyList<PayrollDisbursementLine> Lines);

public sealed record PayrollDisbursementLine(
    Guid EmployeeId,
    string? NationalId,
    string? EmployeeName,
    decimal GrossPay,
    decimal TotalDeductions,
    decimal NetPay,
    string? BankAccount = null);
