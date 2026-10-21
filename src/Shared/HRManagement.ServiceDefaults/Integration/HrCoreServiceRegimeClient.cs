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

    private string ResolvePath(HttpClient client, string relativePath)
    {
        if (client.BaseAddress is not null)
            return relativePath;

        var baseUrl = configuration["Integration:HrCoreBaseUrl"]?.TrimEnd('/')
            ?? "http://localhost:5010";
        return $"{baseUrl}/{relativePath}";
    }

    private sealed record PayrollBlockersRequest(IReadOnlyList<Guid> EmployeeIds);

    private sealed record PayrollBlockersResponse(IReadOnlyList<HrCorePayrollBlockerDto> Blockers);
}

public static class HrCoreServiceRegimeClientExtensions
{
    public static IServiceCollection AddHrCoreServiceRegimeClient(this IServiceCollection services, IConfiguration configuration)
    {
        var hrCoreBase = configuration["Integration:HrCoreBaseUrl"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(hrCoreBase))
            services.AddServiceAuthenticatedClient(HrCoreServiceRegimeClient.HttpClientName, new Uri(hrCoreBase + "/"));
        else
            services.AddHttpClient(HrCoreServiceRegimeClient.HttpClientName);

        services.AddScoped<IHrCoreServiceRegimeClient, HrCoreServiceRegimeClient>();
        return services;
    }
}
