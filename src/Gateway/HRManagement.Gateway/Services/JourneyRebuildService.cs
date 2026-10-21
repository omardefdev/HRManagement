using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Gateway.Services;

public sealed class JourneyRebuildService(
    GatewayAuditDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<JourneyRebuildService> logger) : IJourneyRebuildService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<JourneyRebuildResult> RebuildAsync(
        string correlationId,
        string? authorizationHeader,
        CancellationToken ct = default)
    {
        var corr = correlationId.Trim();
        var httpEvents = await db.AuditEntries.AsNoTracking()
            .Where(e => e.CorrelationId == corr)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .Select(e => new JourneyHttpEventDto
            {
                Id = e.Id,
                OccurredAtUtc = e.OccurredAtUtc,
                Method = e.Method,
                Path = e.Path,
                StatusCode = e.StatusCode,
                Decision = e.Decision,
                Reason = e.Reason
            })
            .ToListAsync(ct);

        var securityEvents = await db.SecurityAuditEvents.AsNoTracking()
            .Where(e => e.CorrelationId == corr)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new JourneySecurityEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                Resource = e.Resource,
                OccurredAtUtc = e.OccurredAtUtc
            })
            .ToListAsync(ct);

        var policyEvents = await FetchPolicyEventsAsync(corr, authorizationHeader, ct);

        var firstHttp = httpEvents.FirstOrDefault();
        var tenantId = await db.AuditEntries.AsNoTracking()
            .Where(e => e.CorrelationId == corr && e.TenantId != null)
            .Select(e => e.TenantId)
            .FirstOrDefaultAsync(ct);
        var scopeId = await db.AuditEntries.AsNoTracking()
            .Where(e => e.CorrelationId == corr && e.ScopeOrgUnitId != null)
            .Select(e => e.ScopeOrgUnitId)
            .FirstOrDefaultAsync(ct);

        var user = await db.AuditEntries.AsNoTracking()
            .Where(e => e.CorrelationId == corr && e.UserName != null)
            .Select(e => e.UserName)
            .FirstOrDefaultAsync(ct);

        var decisions = BuildDecisions(httpEvents, policyEvents, securityEvents);
        var gaps = DetectGaps(corr, httpEvents, policyEvents, securityEvents);

        return new JourneyRebuildResult
        {
            CorrelationId = corr,
            User = user,
            TenantId = tenantId,
            ScopeOrgUnitId = scopeId,
            Decisions = decisions,
            HttpEvents = httpEvents,
            PolicyEvents = policyEvents,
            SecurityEvents = securityEvents,
            Gaps = gaps
        };
    }

    private async Task<List<JourneyPolicyEventDto>> FetchPolicyEventsAsync(
        string correlationId,
        string? authorizationHeader,
        CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("hr-core-journey");
            if (!string.IsNullOrWhiteSpace(authorizationHeader))
            {
                if (AuthenticationHeaderValue.TryParse(authorizationHeader, out var header))
                    client.DefaultRequestHeaders.Authorization = header;
            }

            var response = await client.GetAsync(
                $"api/policies/ledger?correlationId={Uri.EscapeDataString(correlationId)}",
                ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Policy ledger fetch returned {Status} for corr={CorrelationId}",
                    response.StatusCode, correlationId);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var items = await JsonSerializer.DeserializeAsync<List<PolicyLedgerWireDto>>(stream, JsonOptions, ct);
            return (items ?? [])
                .Select(p => new JourneyPolicyEventDto
                {
                    Id = p.Id,
                    Domain = p.Domain ?? "",
                    Action = p.Action ?? "",
                    DecisionJson = p.DecisionJson,
                    OccurredAt = p.OccurredAt
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Policy ledger fetch failed for corr={CorrelationId}", correlationId);
            return [];
        }
    }

    private static List<JourneyDecisionDto> BuildDecisions(
        IReadOnlyList<JourneyHttpEventDto> httpEvents,
        IReadOnlyList<JourneyPolicyEventDto> policyEvents,
        IReadOnlyList<JourneySecurityEventDto> securityEvents)
    {
        var list = new List<JourneyDecisionDto>();
        foreach (var h in httpEvents)
        {
            list.Add(new JourneyDecisionDto
            {
                Source = "gateway-http",
                Decision = h.Decision,
                Detail = $"{h.Method} {h.Path} => {h.StatusCode}",
                At = h.OccurredAtUtc
            });
        }

        foreach (var p in policyEvents)
        {
            list.Add(new JourneyDecisionDto
            {
                Source = "policy-ledger",
                Decision = p.Action,
                Detail = p.DecisionJson,
                At = new DateTimeOffset(p.OccurredAt, TimeSpan.Zero)
            });
        }

        foreach (var s in securityEvents)
        {
            list.Add(new JourneyDecisionDto
            {
                Source = "security-audit",
                Decision = s.EventType,
                Detail = s.Resource,
                At = s.OccurredAtUtc
            });
        }

        return list.OrderBy(d => d.At).ToList();
    }

    private static List<string> DetectGaps(
        string correlationId,
        IReadOnlyList<JourneyHttpEventDto> httpEvents,
        IReadOnlyList<JourneyPolicyEventDto> policyEvents,
        IReadOnlyList<JourneySecurityEventDto> securityEvents)
    {
        var gaps = new List<string>();
        if (httpEvents.Count == 0 && policyEvents.Count == 0 && securityEvents.Count == 0)
            gaps.Add($"no events found for correlationId={correlationId}");

        if (httpEvents.Any(h => h.Decision == "forbidden") && !securityEvents.Any(s => s.EventType.Contains("OrgScope", StringComparison.OrdinalIgnoreCase)))
            gaps.Add("forbidden http without matching OrgScope security audit event");

        return gaps;
    }

    private sealed class PolicyLedgerWireDto
    {
        public Guid Id { get; set; }
        public string? Domain { get; set; }
        public string? Action { get; set; }
        public string? DecisionJson { get; set; }
        public DateTime OccurredAt { get; set; }
    }
}
