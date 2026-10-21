using HRManagement.ServiceDefaults.Integration;
using Payroll.Application.Abstractions;

namespace Payroll.Infrastructure.Integration;

/// <summary>Resolves employee display names via HR.Core batch lookup using a service JWT (IAM-004).</summary>
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
            var result = items.ToDictionary(i => i.Id, i => i.Label);
            foreach (var id in ids)
            {
                if (!result.ContainsKey(id))
                    result[id] = "غير متاح";
            }

            return result;
        }
        catch
        {
            return ids.ToDictionary(id => id, _ => "غير متاح");
        }
    }
}
