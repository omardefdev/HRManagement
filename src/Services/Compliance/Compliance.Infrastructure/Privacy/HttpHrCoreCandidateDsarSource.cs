using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Compliance.Infrastructure.Privacy;

public sealed class HttpHrCoreCandidateDsarSource(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpHrCoreCandidateDsarSource> logger) : IDsarCandidateSubjectSource
{
    public const string ClientName = "DsarHrCore";
    public string SourceName => "HR.Core.Candidate";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<(bool Collected, string? Error, IReadOnlyList<DsarDiscoveryField> Fields)> FetchAsync(
        Guid tenantId,
        Guid? citizenApplicationId,
        string? nationalIdHash,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(ClientName);
            var path = citizenApplicationId is Guid appId
                ? $"api/internal/dsar-subjects/candidates/{appId:D}"
                : $"api/internal/dsar-subjects/candidates/by-hash/{nationalIdHash}";

            using var request = HttpHrCoreDsarSource.CreateTenantRequest(path, tenantId);
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
            logger.LogWarning(ex, "DSAR HR.Core candidate fetch failed for {ApplicationId}/{Hash}",
                citizenApplicationId, nationalIdHash);
            return (false, ex.Message, []);
        }
    }
}
