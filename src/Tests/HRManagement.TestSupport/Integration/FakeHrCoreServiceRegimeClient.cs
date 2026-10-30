using HRManagement.SharedKernel.Legal;

namespace HRManagement.TestSupport.Integration;

/// <summary>
/// QA-034: declared stand-in for the HR.Core payroll-blocker S2S client in Payroll integration hosts. By default no
/// employee is blocked; ids added to <see cref="BlockedEmployeeIds"/> come back <c>Blocked</c> with
/// <see cref="ReasonCode"/> (the blocked path itself is proven in PLAN-59).
/// </summary>
public sealed class FakeHrCoreServiceRegimeClient : IHrCoreServiceRegimeClient
{
    public HashSet<Guid> BlockedEmployeeIds { get; } = [];

    public string ReasonCode { get; set; } = "REGIME_EXCEPTION_OPEN";

    public Task<IReadOnlyList<HrCorePayrollBlockerDto>> GetPayrollBlockersAsync(
        IReadOnlyList<Guid> employeeIds,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<HrCorePayrollBlockerDto>>(employeeIds
            .Select(id => BlockedEmployeeIds.Contains(id)
                ? new HrCorePayrollBlockerDto(id, true, ReasonCode)
                : new HrCorePayrollBlockerDto(id, false, null))
            .ToList());
}
