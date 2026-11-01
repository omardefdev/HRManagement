using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HRManagement.SharedKernel.ViewModels;

namespace HRManagement.ServiceDefaults.Integration;

public sealed record HrCoreLookupItem(Guid Id, string Label, string? Sublabel);

public interface IHrCoreLookupClient
{
    Task<IReadOnlyList<HrCoreLookupItem>> GetEmployeesByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<HrCoreLookupItem>> GetOrgUnitsByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
}

/// <summary>S2S batch lookup against HR.Core <c>/api/lookup/*/by-ids</c> (IAM-004).</summary>
public sealed class HrCoreLookupClient(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IHrCoreLookupClient
{
    public const string HttpClientName = "HrCoreLookup";
    public const int MaxBatchSize = SearchFilterBase.LookupMaxPageSize;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<IReadOnlyList<HrCoreLookupItem>> GetEmployeesByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        FetchByIdsAsync("api/lookup/employees/by-ids", ids, ct);

    public Task<IReadOnlyList<HrCoreLookupItem>> GetOrgUnitsByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        FetchByIdsAsync("api/lookup/org-units/by-ids", ids, ct);

    private async Task<IReadOnlyList<HrCoreLookupItem>> FetchByIdsAsync(
        string relativePath,
        IReadOnlyList<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        var distinct = ids.Distinct().ToList();
        var client = httpClientFactory.CreateClient(HttpClientName);
        var items = new List<HrCoreLookupItem>();

        foreach (var chunk in distinct.Chunk(MaxBatchSize))
        {
            var path = ResolvePath(client, relativePath);
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(new LookupByIdsRequest(chunk))
            };
            GatewayCallerProof.Sign(request, configuration, "/" + relativePath.TrimStart('/'));
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<LookupBatchResponse>(JsonOptions, ct);
            if (page?.Items is { Count: > 0 })
                items.AddRange(page.Items);
        }

        return items;
    }

    // PLAN-37: no localhost fallback; the client is registered with its base address (Integration:HrCoreBaseUrl).
    private static string ResolvePath(HttpClient client, string relativePath) =>
        client.BaseAddress is not null
            ? relativePath
            : throw new InvalidOperationException($"{HttpClientName} has no base address; register it with {nameof(HrCoreEndpoint)} ({HrCoreEndpoint.Key}).");

    private sealed record LookupByIdsRequest(IReadOnlyList<Guid> Ids);

    private sealed record LookupBatchResponse(IReadOnlyList<HrCoreLookupItem> Items);
}

public static class HrCoreLookupClientExtensions
{
    public static IServiceCollection AddHrCoreLookupClient(this IServiceCollection services, IConfiguration configuration)
    {
        // PLAN-37: the HR.Core URL is required (start-up error naming Integration:HrCoreBaseUrl outside Development).
        services.AddServiceEndpointWarnings();
        services.AddServiceAuthenticatedClient(HrCoreLookupClient.HttpClientName, HrCoreEndpoint.RequireBaseUri(configuration));

        services.AddScoped<IHrCoreLookupClient, HrCoreLookupClient>();
        return services;
    }
}
