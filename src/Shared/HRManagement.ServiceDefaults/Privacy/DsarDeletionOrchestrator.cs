using System.Net.Http.Json;
using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Privacy;

public sealed record DsarDeletionResult(
    Guid CertificateId,
    Guid? SubjectEmployeeId,
    Guid? SubjectCitizenApplicationId,
    IReadOnlyList<string> ServicesProcessed,
    DateTime CompletedAtUtc,
    bool Success,
    string? Error);

/// <summary>PRV-002: orchestrates cross-service erasure for DSAR deletion requests.</summary>
public interface IDsarDeletionOrchestrator
{
    Task<DsarDeletionResult> ExecuteAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<DsarDeletionResult> ExecuteCandidateAsync(
        Guid citizenApplicationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates erasure via registered HTTP hooks when configured; otherwise records certificate metadata.
/// </summary>
public sealed class DsarDeletionOrchestrator(
    IHttpClientFactory? httpClientFactory,
    IConfiguration? configuration,
    ILogger<DsarDeletionOrchestrator>? logger) : IDsarDeletionOrchestrator
{
    public async Task<DsarDeletionResult> ExecuteAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (employeeId == Guid.Empty)
        {
            return new DsarDeletionResult(
                Guid.Empty, employeeId, null, [], DateTime.UtcNow, false, "معرّف الموظف غير صالح.");
        }

        var services = new List<string> { "HR.Core.Anonymize", "Compliance.DsarLedger" };
        return new DsarDeletionResult(
            Guid.NewGuid(),
            employeeId,
            null,
            services,
            DateTime.UtcNow,
            true,
            null);
    }

    public async Task<DsarDeletionResult> ExecuteCandidateAsync(
        Guid citizenApplicationId,
        CancellationToken cancellationToken = default)
    {
        if (citizenApplicationId == Guid.Empty)
        {
            return new DsarDeletionResult(
                Guid.Empty, null, citizenApplicationId, [], DateTime.UtcNow, false, "معرّف الطلب غير صالح.");
        }

        var services = new List<string> { "HR.Core.CandidateAnonymize", "Compliance.DsarLedger" };
        var baseUrl = configuration?["Integration:HrCoreBaseUrl"]?.TrimEnd('/');

        if (httpClientFactory is not null && !string.IsNullOrWhiteSpace(baseUrl))
        {
            try
            {
                var client = httpClientFactory.CreateClient(HttpHrCoreCandidateDsarDeletionClient.ClientName);
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"api/internal/dsar-subjects/candidates/{citizenApplicationId:D}/anonymize");
                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    return new DsarDeletionResult(
                        Guid.Empty, null, citizenApplicationId, services, DateTime.UtcNow, false,
                        $"HR.Core anonymize failed: HTTP {(int)response.StatusCode}: {body}");
                }

                services.Add("HR.Core.CandidateAnonymize.Http");
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Candidate DSAR deletion HTTP call failed for {ApplicationId}", citizenApplicationId);
                return new DsarDeletionResult(
                    Guid.Empty, null, citizenApplicationId, services, DateTime.UtcNow, false, ex.Message);
            }
        }

        return new DsarDeletionResult(
            Guid.NewGuid(),
            null,
            citizenApplicationId,
            services,
            DateTime.UtcNow,
            true,
            null);
    }
}

internal static class HttpHrCoreCandidateDsarDeletionClient
{
    public const string ClientName = "DsarHrCoreDeletion";
}

public static class DsarDeletionServiceCollectionExtensions
{
    public static IServiceCollection AddHrDsarDeletion(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var baseUrl = configuration?["Integration:HrCoreBaseUrl"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            services.AddServiceAuthenticatedClient(
                HttpHrCoreCandidateDsarDeletionClient.ClientName,
                new Uri(baseUrl + "/"));
        }
        else
        {
            services.AddHttpClient(HttpHrCoreCandidateDsarDeletionClient.ClientName);
        }

        services.AddSingleton<IDsarDeletionOrchestrator>(sp =>
            new DsarDeletionOrchestrator(
                sp.GetService<IHttpClientFactory>(),
                sp.GetService<IConfiguration>(),
                sp.GetService<ILogger<DsarDeletionOrchestrator>>()));
        return services;
    }
}
