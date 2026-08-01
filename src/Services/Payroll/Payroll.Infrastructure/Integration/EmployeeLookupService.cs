using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Payroll.Application.Abstractions;

namespace Payroll.Infrastructure.Integration;

/// <summary>Resolves employee display names via HR.Core lookup using a service JWT (IAM-004).</summary>
public sealed class EmployeeLookupService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IEmployeeLookup
{
    public const string HttpClientName = "HrCoreLookup";

    public async Task<string?> GetEmployeeDisplayNameAsync(Guid employeeId, CancellationToken ct)
    {
        var dict = await GetEmployeeDisplayNamesAsync([employeeId], ct);
        return dict.TryGetValue(employeeId, out var name) ? name : null;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetEmployeeDisplayNamesAsync(
        IEnumerable<Guid> employeeIds,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, string>();
        var client = httpClientFactory.CreateClient(HttpClientName);

        foreach (var id in employeeIds.Distinct())
        {
            try
            {
                // Prefer named-client BaseAddress; fall back to Integration:HrCoreBaseUrl for legacy config.
                var path = client.BaseAddress is not null
                    ? $"api/lookup/employees/{id}"
                    : $"{(configuration["Integration:HrCoreBaseUrl"]?.TrimEnd('/') ?? "http://localhost:5010")}/api/lookup/employees/{id}";

                var item = await client.GetFromJsonAsync<LookupItemDto>(path, ct);
                if (item is not null)
                    result[id] = item.Label;
            }
            catch
            {
                result[id] = id.ToString()[..8];
            }
        }

        return result;
    }

    private sealed record LookupItemDto(Guid Id, string Label, string? Sublabel);
}
