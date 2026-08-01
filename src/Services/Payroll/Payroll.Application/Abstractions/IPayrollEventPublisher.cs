namespace Payroll.Application.Abstractions;

public interface IPayrollEventPublisher
{
    Task PublishPayrollProcessedAsync(
        Guid payrollRunId,
        DateTime periodStart,
        DateTime periodEnd,
        decimal totalNetPay,
        CancellationToken ct);
}

public interface IEmployeeLookup
{
    Task<string?> GetEmployeeDisplayNameAsync(Guid employeeId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> GetEmployeeDisplayNamesAsync(
        IEnumerable<Guid> employeeIds,
        CancellationToken ct);
}
