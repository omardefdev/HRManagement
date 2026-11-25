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
    string? BankAccount = null,
    // PLAN-56 (G-PAY-03): the line's adjustments; gross − deductions + adjustments = net. Last, so that older callers keep their shape.
    decimal TotalAdjustments = 0m);
