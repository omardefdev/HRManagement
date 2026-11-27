using Payroll.Domain.Enums;

namespace Payroll.Application.Dtos;

public sealed record PayrollRunDetailsDto(
    Guid Id,
    Guid TenantId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime RunDate,
    PayrollRunStatus Status,
    PayrollRunType RunType,
    Guid? SourceRunId,
    Guid? PayrollConfigurationId,
    string? ConfigContentHash,
    decimal TotalNetPay,
    bool ResultsFrozen,
    bool ValidatedAtCalculate,
    IReadOnlyList<SalaryLineDetailsDto> SalaryLines,
    // PLAN-59 (P-9): what an SQL-free cycle reads of the run — its present version, the approvals that version holds and needs (as the
    // approval counts them: different approvers; the count from the agency's threshold), and the posting's disbursement batch (null
    // before the posting). Added at the end of the shape.
    int Revision = 1,
    int ApprovalsGiven = 0,
    int ApprovalsRequired = 1,
    Guid? PaymentBatchId = null);

public sealed record SalaryLineDetailsDto(
    Guid Id,
    Guid TenantId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    Guid PayrollRunId,
    Guid EmployeeId,
    decimal GrossPay,
    decimal TotalDeductions,
    decimal NetPay,
    string? PayComponents,
    // PLAN-56 (G-PAY-03): gross − deductions + adjustments = net, floored at zero. Added at the end of the shape.
    decimal TotalAdjustments);
