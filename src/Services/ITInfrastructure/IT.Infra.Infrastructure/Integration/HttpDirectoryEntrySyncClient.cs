using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace IT.Infra.Infrastructure.Integration;

/// <summary>
/// S2S client for Auth.Directory internal sync using service JWT (IAM-004).
/// Falls back to no-op success when base URL is not configured.
/// </summary>
public class HttpDirectoryEntrySyncClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<HttpDirectoryEntrySyncClient> logger) : IDirectoryEntrySyncService
{
    public const string HttpClientName = nameof(HttpDirectoryEntrySyncClient);

    public async Task<bool> SyncEmployeeCreatedAsync(
        Guid employeeId,
        string fullName,
        string? nationalId,
        Guid? primaryOrgUnitId,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = configuration["Services:AuthDirectory:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogDebug("Auth.Directory BaseUrl not configured; skipping directory sync for {EmployeeId}", employeeId);
            return true;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsJsonAsync("api/internal/directory/sync-employee", new
            {
                employeeId,
                fullName,
                nationalId,
                primaryOrgUnitId
            }, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Directory sync HTTP {Status} for EmployeeId={EmployeeId}",
                    response.StatusCode, employeeId);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Directory sync failed for EmployeeId={EmployeeId}", employeeId);
            return false;
        }
    }
}

public static class DirectorySyncClientExtensions
{
    /// <summary>
    /// PLAN-37: the Auth.Directory sync client (<c>Services:AuthDirectory:BaseUrl</c>, scope <c>directory.sync</c>),
    /// one named registration so the wiring matrix can name it.
    /// </summary>
    public static IServiceCollection AddDirectorySyncClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceIdentityClient();
        var authDirectoryBase = configuration["Services:AuthDirectory:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(authDirectoryBase))
        {
            services.AddServiceAuthenticatedClient(
                HttpDirectoryEntrySyncClient.HttpClientName,
                new Uri(authDirectoryBase.TrimEnd('/') + "/"));
        }
        else
        {
            services.AddHttpClient(HttpDirectoryEntrySyncClient.HttpClientName);
        }

        services.AddScoped<IDirectoryEntrySyncService, HttpDirectoryEntrySyncClient>();
        return services;
    }
}
