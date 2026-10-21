using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Compliance.Infrastructure.Privacy;

public sealed class HttpHrCoreDsarSource(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpHrCoreDsarSource> logger) : IDsarSubjectSource
{
    public const string ClientName = "DsarHrCore";
    public string SourceName => "HR.Core";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<(bool Collected, string? Error, IReadOnlyList<DsarDiscoveryField> Fields)> FetchAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(ClientName);
            using var request = CreateTenantRequest(
                $"api/internal/dsar-subjects/{employeeId:D}", tenantId);
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return (false, $"HTTP {(int)response.StatusCode}: {body}", []);
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
            return (true, null, FlattenJson(SourceName, doc));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DSAR HR.Core fetch failed for {EmployeeId}", employeeId);
            return (false, ex.Message, []);
        }
    }

    internal static List<DsarDiscoveryField> FlattenJson(string source, JsonElement element, string prefix = "")
    {
        var fields = new List<DsarDiscoveryField>();
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var name = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    fields.AddRange(FlattenJson(source, prop.Value, name));
                }
                break;
            case JsonValueKind.Array:
                fields.Add(new DsarDiscoveryField(source, prefix, element.GetRawText()));
                break;
            default:
                fields.Add(new DsarDiscoveryField(source, prefix, element.ToString()));
                break;
        }

        return fields;
    }

    internal static HttpRequestMessage CreateTenantRequest(string path, Guid tenantId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(TenantIdMiddleware.HeaderName, tenantId.ToString("D"));
        return request;
    }
}

public sealed class HttpPayrollDsarSource(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpPayrollDsarSource> logger) : IDsarSubjectSource
{
    public const string ClientName = "DsarPayroll";
    public string SourceName => "Payroll";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<(bool Collected, string? Error, IReadOnlyList<DsarDiscoveryField> Fields)> FetchAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(ClientName);
            using var request = HttpHrCoreDsarSource.CreateTenantRequest(
                $"api/internal/dsar-subjects/{employeeId:D}", tenantId);
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return (false, $"HTTP {(int)response.StatusCode}: {body}", []);
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
            return (true, null, HttpHrCoreDsarSource.FlattenJson(SourceName, doc));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DSAR Payroll fetch failed for {EmployeeId}", employeeId);
            return (false, ex.Message, []);
        }
    }
}

public sealed class HttpFileStorageDsarSource(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpFileStorageDsarSource> logger) : IDsarSubjectSource
{
    public const string ClientName = "DsarFileStorage";
    public string SourceName => "File.Storage";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<(bool Collected, string? Error, IReadOnlyList<DsarDiscoveryField> Fields)> FetchAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(ClientName);
            using var request = HttpHrCoreDsarSource.CreateTenantRequest(
                $"api/internal/dsar-subjects/{employeeId:D}/files", tenantId);
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return (false, $"HTTP {(int)response.StatusCode}: {body}", []);
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
            return (true, null, HttpHrCoreDsarSource.FlattenJson(SourceName, doc));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DSAR File.Storage fetch failed for {EmployeeId}", employeeId);
            return (false, ex.Message, []);
        }
    }
}

public static class ComplianceDsarCollectorExtensions
{
    public static IServiceCollection AddComplianceDsarCollector(this IServiceCollection services, IConfiguration configuration)
    {
        RegisterClient(services, configuration, HttpHrCoreDsarSource.ClientName, "Integration:HrCoreBaseUrl");
        RegisterClient(services, configuration, HttpPayrollDsarSource.ClientName, "Integration:PayrollBaseUrl");
        RegisterClient(services, configuration, HttpFileStorageDsarSource.ClientName, "Integration:FileStorageBaseUrl");

        services.AddScoped<IDsarSubjectSource, HttpHrCoreDsarSource>();
        services.AddScoped<IDsarSubjectSource, HttpPayrollDsarSource>();
        services.AddScoped<IDsarSubjectSource, HttpFileStorageDsarSource>();
        services.AddScoped<IDsarCandidateSubjectSource, HttpHrCoreCandidateDsarSource>();
        services.AddScoped<IDsarDataCollectorService, DsarDataCollectorService>();
        return services;
    }

    private static void RegisterClient(IServiceCollection services, IConfiguration configuration, string name, string configKey)
    {
        var baseUrl = configuration[configKey]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(baseUrl))
            services.AddServiceAuthenticatedClient(name, new Uri(baseUrl + "/"));
        else
            services.AddHttpClient(name);
    }
}
