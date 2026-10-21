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
    IReadOnlyList<SalaryLineDetailsDto> SalaryLines);

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
    string? PayComponents);
