using System.Text.Json;
using HR.Contracts.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace HR.Contracts.Infrastructure.Services;

/// <summary>Default: allow publish (tests / when HR.Core BaseUrl unset).</summary>
public sealed class PassThroughEmployeeLifecycleGate : IEmployeeLifecycleGate
{
    public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
        Task.FromResult<(bool, string?)>((true, null));
}

/// <summary>
/// FUN-002-B: query HR.Core's legal-hold flag. PLAN-37: the client carries the HR.Core base address from
/// <c>Integration:HrCoreBaseUrl</c> (AddEmployeeLifecycleGate); its authentication and fail-closed behaviour are PLAN-42.
/// </summary>
public sealed class HttpEmployeeLifecycleGate(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpEmployeeLifecycleGate> logger) : IEmployeeLifecycleGate
{
    public const string HttpClientName = "hr-core-lifecycle";

    public async Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var url = $"api/internal/employees/{employeeId}/lifecycle-flags";
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Legal-hold check HTTP {Status} for {EmployeeId}; allowing publish",
                    (int)response.StatusCode, employeeId);
                return (true, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var hold = doc.RootElement.TryGetProperty("legalHold", out var p) && p.GetBoolean();
            if (hold)
                return (false, "الموظف تحت حجز قانوني (Legal Hold) — لا يمكن نشر الفصل.");
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Legal-hold check failed for {EmployeeId}; allowing publish", employeeId);
            return (true, null);
        }
    }
}
