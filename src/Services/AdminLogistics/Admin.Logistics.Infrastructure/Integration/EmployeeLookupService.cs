using Admin.Logistics.Infrastructure.Abstractions;
using HRManagement.ServiceDefaults.Integration;

namespace Admin.Logistics.Infrastructure.Integration;

public sealed class EmployeeLookupService(IHrCoreLookupClient lookupClient) : IEmployeeLookup
{
    public async Task<string?> GetEmployeeDisplayNameAsync(Guid employeeId, CancellationToken ct)
    {
        var dict = await GetEmployeeDisplayNamesAsync([employeeId], ct);
        return dict.TryGetValue(employeeId, out var name) ? name : null;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetEmployeeDisplayNamesAsync(
        IEnumerable<Guid> employeeIds,
        CancellationToken ct)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        try
        {
            var items = await lookupClient.GetEmployeesByIdsAsync(ids, ct);
            return items.ToDictionary(i => i.Id, FormatLookupLabel);
        }
        catch
        {
            // Client-side display-names.js hydrates [data-hr-employee-id] after load.
            return new Dictionary<Guid, string>();
        }
    }

    private static string FormatLookupLabel(HrCoreLookupItem item) =>
        string.IsNullOrWhiteSpace(item.Sublabel) ? item.Label : $"{item.Label} — {item.Sublabel}";
}
