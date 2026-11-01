using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace IT.Infra.Infrastructure.Integration;

/// <summary>
/// S2S client for Auth.Directory internal sync using service JWT (IAM-004). PLAN-37: the base URL is required at
/// registration (<see cref="DirectorySyncClientExtensions.AddDirectorySyncClient"/>); there is no silent no-op success.
/// </summary>
public class HttpDirectoryEntrySyncClient(
    IHttpClientFactory httpClientFactory,
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
    public const string BaseUrlKey = "Services:AuthDirectory:BaseUrl";
    public const string DevelopmentDefault = "http://localhost:5130";

    /// <summary>
    /// PLAN-37: the Auth.Directory sync client (<c>Services:AuthDirectory:BaseUrl</c>, scope <c>directory.sync</c>),
    /// one named registration so the wiring matrix can name it.
    /// </summary>
    public static IServiceCollection AddDirectorySyncClient(this IServiceCollection services, IConfiguration configuration)
    {
        // Required URL: a start-up error names the key outside Development (no silent "synced" without a call).
        var authDirectoryBase = ServiceEndpoint.Require(configuration, BaseUrlKey, DevelopmentDefault);
        services.AddServiceEndpointWarnings();
        services.AddServiceAuthenticatedClient(HttpDirectoryEntrySyncClient.HttpClientName, new Uri(authDirectoryBase + "/"));
        services.AddScoped<IDirectoryEntrySyncService, HttpDirectoryEntrySyncClient>();
        return services;
    }
}
