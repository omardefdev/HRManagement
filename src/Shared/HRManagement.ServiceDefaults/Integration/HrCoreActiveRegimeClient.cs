using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.SharedKernel.Legal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Integration;

/// <summary>SYR-008: S2S client for HR.Core active service regime lookup.</summary>
public sealed class HrCoreActiveRegimeClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IHrCoreActiveRegimeClient
{
    public const string HttpClientName = "HrCoreActiveRegime";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<HrCoreActiveRegimeDto>> GetActiveRegimesAsync(
        IReadOnlyList<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];

        var client = httpClientFactory.CreateClient(HttpClientName);
        const string relativePath = "api/internal/service-regime/active-regimes";
        using var request = new HttpRequestMessage(HttpMethod.Post, ResolvePath(client, relativePath))
        {
            Content = JsonContent.Create(new ActiveRegimesRequest(employeeIds))
        };
        GatewayCallerProof.Sign(request, configuration, "/" + relativePath);

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ActiveRegimesResponse>(JsonOptions, ct);
        return payload?.Regimes ?? [];
    }

    private string ResolvePath(HttpClient client, string relativePath)
    {
        if (client.BaseAddress is not null)
            return relativePath;

        var baseUrl = configuration["Integration:HrCoreBaseUrl"]?.TrimEnd('/')
            ?? "http://localhost:5010";
        return $"{baseUrl}/{relativePath}";
    }

    private sealed record ActiveRegimesRequest(IReadOnlyList<Guid> EmployeeIds);

    private sealed record ActiveRegimesResponse(IReadOnlyList<HrCoreActiveRegimeDto> Regimes);
}

public static class HrCoreActiveRegimeClientExtensions
{
    public static IServiceCollection AddHrCoreActiveRegimeClient(this IServiceCollection services, IConfiguration configuration)
    {
        var hrCoreBase = configuration["Integration:HrCoreBaseUrl"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(hrCoreBase))
            services.AddServiceAuthenticatedClient(HrCoreActiveRegimeClient.HttpClientName, new Uri(hrCoreBase + "/"));
        else
            services.AddHttpClient(HrCoreActiveRegimeClient.HttpClientName);

        services.AddScoped<IHrCoreActiveRegimeClient, HrCoreActiveRegimeClient>();
        return services;
    }
}
