namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-007: S2S payroll blocker lookup against HR.Core service regime governance.</summary>
public interface IHrCoreServiceRegimeClient
{
    Task<IReadOnlyList<HrCorePayrollBlockerDto>> GetPayrollBlockersAsync(
        IReadOnlyList<Guid> employeeIds,
        CancellationToken ct = default);
}

public sealed record HrCorePayrollBlockerDto(Guid EmployeeId, bool Blocked, string? ReasonCode);
