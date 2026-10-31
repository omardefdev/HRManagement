using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Policy;

public interface IPolicyEnforceClient
{
    Task<PolicyEnforceResponse> EnforceAsync(PolicyEnforceRequest request, CancellationToken ct = default);
    Task<PolicyVersionSnapshot?> GetSnapshotAsync(string domain, Guid? tenantId, CancellationToken ct = default);
}

public sealed class PolicyEnforceClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IPolicySnapshotStore snapshotStore,
    ILogger<PolicyEnforceClient> logger) : IPolicyEnforceClient
{
    public const string HttpClientName = "hr-core-policy";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<PolicyEnforceResponse> EnforceAsync(PolicyEnforceRequest request, CancellationToken ct = default)
    {
        var mode = configuration[$"PolicyEngine:{request.Domain}"] ?? configuration["PolicyEngine:DefaultMode"] ?? "Off";
        if (Enum.TryParse<PolicyEnforcementMode>(mode, true, out var parsed))
            request.Mode = parsed;

        if (request.Mode == PolicyEnforcementMode.Off)
        {
            return new PolicyEnforceResponse
            {
                Allowed = true,
                Decision = PolicyStaticFallbackShim.Evaluate(request),
                Explanations = ["PolicyEngine:Off — static shim"]
            };
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsJsonAsync("/api/internal/policies/enforce", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Policy enforce HTTP {Status}", response.StatusCode);
                return Degraded(request);
            }

            var body = await response.Content.ReadFromJsonAsync<PolicyEnforceResponse>(JsonOptions, ct);
            if (body?.Decision?.PolicyVersionId is not null)
            {
                var snap = await GetSnapshotAsync(request.Domain, request.TenantId, ct);
                if (snap is not null)
                    snapshotStore.Set(request.Domain, snap);
            }
            return body ?? Degraded(request);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Policy enforce failed — degraded snapshot");
            return Degraded(request);
        }
    }

    public async Task<PolicyVersionSnapshot?> GetSnapshotAsync(string domain, Guid? tenantId, CancellationToken ct = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var url = $"/api/internal/policies/snapshots/{Uri.EscapeDataString(domain)}";
            if (tenantId.HasValue) url += $"?tenantId={tenantId}";
            return await client.GetFromJsonAsync<PolicyVersionSnapshot>(url, JsonOptions, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Policy snapshot fetch failed for {Domain}", domain);
            return null;
        }
    }

    private PolicyEnforceResponse Degraded(PolicyEnforceRequest request)
    {
        var cached = snapshotStore.Get(request.Domain);
        if (cached is not null)
        {
            var decision = PolicyEvaluator.Evaluate(cached, request.Facts, request.Action);
            return new PolicyEnforceResponse
            {
                Allowed = decision.Allowed,
                Effects = decision.Effects.Select(e => e.ToString()).ToList(),
                Explanations = decision.Explanations.Concat(["degraded: embedded snapshot"]).ToList(),
                Decision = decision,
                PolicyVersionId = decision.PolicyVersionId
            };
        }

        var shim = PolicyStaticFallbackShim.Evaluate(request);
        return new PolicyEnforceResponse
        {
            Allowed = shim.Allowed,
            Effects = shim.Effects.Select(e => e.ToString()).ToList(),
            Explanations = shim.Explanations,
            Decision = shim
        };
    }
}

/// <summary>Lightweight static shim for client-side degraded mode (mirrors HRCore PolicyStaticFallback).</summary>
public static class PolicyStaticFallbackShim
{
    public static PolicyDecision Evaluate(PolicyEnforceRequest request)
    {
        if (string.Equals(request.Action, "Classify", StringComparison.OrdinalIgnoreCase))
            return EvaluateClassify(request);

        var isPrivileged = GetBool(request.Facts, "isPrivileged");
        var value = GetDecimal(request.Facts, "value");
        var effects = new List<PolicyEffectDefinition>();
        if (isPrivileged)
            effects.Add(new PolicyEffectDefinition { Effect = PolicyEffect.RequireEvidence });
        if (isPrivileged || value > 10_000m)
            effects.Add(new PolicyEffectDefinition
            {
                Effect = PolicyEffect.TriggerWorkflow,
                Params = new Dictionary<string, string> { ["workflowCode"] = "ASSET-ASSIGN-APPROVAL" }
            });
        return new PolicyDecision
        {
            Allowed = true,
            Effects = effects.Select(e => e.Effect).Distinct().ToList(),
            EffectDetails = effects,
            Explanations = ["degraded static shim"]
        };
    }

    private static PolicyDecision EvaluateClassify(PolicyEnforceRequest request)
    {
        var value = GetDecimal(request.Facts, "value");
        var category = GetString(request.Facts, "category");
        var effects = new List<PolicyEffectDefinition>();

        if (value > 10_000m || (category == "أصول ثابتة" && value > 5_000m))
        {
            effects.Add(new PolicyEffectDefinition
            {
                Effect = PolicyEffect.SetField,
                Params = new Dictionary<string, string>
                {
                    ["field"] = "isPrivileged",
                    ["value"] = "true"
                }
            });
        }

        return new PolicyDecision
        {
            Allowed = true,
            Effects = effects.Count > 0 ? effects.Select(e => e.Effect).Distinct().ToList() : [PolicyEffect.Allow],
            EffectDetails = effects,
            Explanations = ["degraded static shim: Classify"]
        };
    }

    private static string GetString(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var v) ? v : "";

    private static bool GetBool(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static decimal GetDecimal(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var v) && decimal.TryParse(v, out var d) ? d : 0m;
}

public static class PolicyEnforceClientExtensions
{
    public static IServiceCollection AddHrPolicyEnforceClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<IPolicySnapshotStore, PolicySnapshotStore>();
        services.AddScoped<IPolicyEnforceClient, PolicyEnforceClient>();
        // PLAN-37: the HR.Core URL is required (start-up error naming Integration:HrCoreBaseUrl outside Development).
        services.AddServiceEndpointWarnings();
        services.AddServiceAuthenticatedClient(PolicyEnforceClient.HttpClientName, HrCoreEndpoint.RequireBaseUri(configuration));
        return services;
    }

    public static IServiceCollection AddHrPolicyPublishedConsumer(this IServiceCollection services)
    {
        services.AddScoped<PolicyPublishedConsumer>();
        return services;
    }
}
