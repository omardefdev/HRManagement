namespace Admin.Logistics.Infrastructure.Abstractions;

public interface IEmployeeLookup
{
    Task<string?> GetEmployeeDisplayNameAsync(Guid employeeId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> GetEmployeeDisplayNamesAsync(
        IEnumerable<Guid> employeeIds,
        CancellationToken ct);
}
