using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.SharedKernel.Legal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Integration;

/// <summary>SYR-007: S2S client for HR.Core service regime payroll blockers.</summary>
public sealed class HrCoreServiceRegimeClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IHrCoreServiceRegimeClient
{
    public const string HttpClientName = "HrCoreServiceRegime";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<HrCorePayrollBlockerDto>> GetPayrollBlockersAsync(
        IReadOnlyList<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];

        var client = httpClientFactory.CreateClient(HttpClientName);
        var relativePath = "api/internal/service-regime/payroll-blockers";
        using var request = new HttpRequestMessage(HttpMethod.Post, ResolvePath(client, relativePath))
        {
            Content = JsonContent.Create(new PayrollBlockersRequest(employeeIds))
        };
        GatewayCallerProof.Sign(request, configuration, "/" + relativePath);

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<PayrollBlockersResponse>(JsonOptions, ct);
        return payload?.Blockers ?? [];
    }

    // PLAN-37: no localhost fallback; the client is registered with its base address (Integration:HrCoreBaseUrl).
    private static string ResolvePath(HttpClient client, string relativePath) =>
        client.BaseAddress is not null
            ? relativePath
            : throw new InvalidOperationException($"{HttpClientName} has no base address; register it with {nameof(HrCoreEndpoint)} ({HrCoreEndpoint.Key}).");

    private sealed record PayrollBlockersRequest(IReadOnlyList<Guid> EmployeeIds);

    private sealed record PayrollBlockersResponse(IReadOnlyList<HrCorePayrollBlockerDto> Blockers);
}

public static class HrCoreServiceRegimeClientExtensions
{
    public static IServiceCollection AddHrCoreServiceRegimeClient(this IServiceCollection services, IConfiguration configuration)
    {
        // PLAN-37: the HR.Core URL is required (start-up error naming Integration:HrCoreBaseUrl outside Development).
        services.AddServiceEndpointWarnings();
        services.AddServiceAuthenticatedClient(HrCoreServiceRegimeClient.HttpClientName, HrCoreEndpoint.RequireBaseUri(configuration));

        services.AddScoped<IHrCoreServiceRegimeClient, HrCoreServiceRegimeClient>();
        return services;
    }
}
