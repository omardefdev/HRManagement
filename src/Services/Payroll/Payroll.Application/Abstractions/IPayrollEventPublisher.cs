using HRManagement.ServiceDefaults.Messaging;

namespace Payroll.Application.Abstractions;

public interface IPayrollEventPublisher
{
    [StagesOutboxMessages]
    Task PublishPayrollProcessedAsync(
        Guid payrollRunId,
        DateTime periodStart,
        DateTime periodEnd,
        decimal totalNetPay,
        CancellationToken ct);

    /// <summary>
    /// PLAN-56 (G-PAY-06): an approved reversal of a posted run. Staged in the outbox of the reversal's own transaction, before its
    /// one save (PLAN-40's SingleSave). <paramref name="reversedNetPay"/> is the total the posting published: the amount taken back.
    /// </summary>
    [StagesOutboxMessages]
    Task PublishPayrollReversedAsync(
        Guid payrollRunId,
        DateTime periodStart,
        DateTime periodEnd,
        decimal reversedNetPay,
        Guid reversalRequestId,
        DateTime reversedAt,
        CancellationToken ct);
}

/// <summary>PLAN-59: what a lookup answers for an employee it cannot name (the caller may show his id instead).</summary>
public static class EmployeeLookupLabels
{
    public const string Unavailable = "غير متاح";
}

public interface IEmployeeLookup
{
    Task<string?> GetEmployeeDisplayNameAsync(Guid employeeId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> GetEmployeeDisplayNamesAsync(
        IEnumerable<Guid> employeeIds,
        CancellationToken ct);
}
