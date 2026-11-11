using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace HRManagement.ServiceDefaults.Policy;

public interface IPolicyEnforceClient
{
    Task<PolicyEnforceResponse> EnforceAsync(PolicyEnforceRequest request, CancellationToken ct = default);
}

/// <summary>
/// PLAN-42 (G-POL-03): the remote policy decision.
/// <list type="number">
/// <item>Off: no call. The custody shim for <c>Assets.Custody</c>, a neutral allow for every other domain.</item>
/// <item>Shadow: HR.Core is called, the caller always gets the Off result, and the server's decision is kept in
/// <see cref="PolicyEnforceResponse.ShadowDecision"/>. A failure counts and warns, it never blocks.</item>
/// <item>Enforce: a 200 stands as it is, allow or deny. Anything else follows the domain's failure mode: refused by
/// default (<see cref="PolicyFailureMode.FailClosed"/>).</item>
/// <item>The server's <see cref="PolicyEnforceResponse.EffectiveMode"/> decides how a 200 is read; the local mode decides
/// the failure path only. A difference warns and counts.</item>
/// <item>No retry beyond <c>AddHrOutboundResilience</c>.</item>
/// <item>No local copy of any policy: nothing can leak between tenants. The tenant travels in <c>X-Tenant-ID</c>.</item>
/// </list>
/// </summary>
public sealed class PolicyEnforceClient(
    IHttpClientFactory httpClientFactory,
    IPolicyModeResolver modeResolver,
    PolicyClientMetrics metrics,
    ILogger<PolicyEnforceClient> logger) : IPolicyEnforceClient
{
    public const string HttpClientName = "hr-core-policy";
    public const string EnforcePath = "/api/internal/policies/enforce";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<PolicyEnforceResponse> EnforceAsync(PolicyEnforceRequest request, CancellationToken ct = default)
    {
        var localMode = modeResolver.Resolve(request.Domain);
        request.Mode = localMode;

        if (localMode == PolicyEnforcementMode.Off)
            return OffResponse(request);

        // PLAN-49: what the call costs the action that makes it. Shadow asks HR.Core on every asset, card and leave action and
        // changes no outcome, so this duration is the whole price of Shadow; one histogram sample and one line per call
        // (fixed names and a number: the domain and the action come from the service's own code, never from a request).
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var (reply, failure) = await CallAsync(request, ct);
        var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var outcome = reply is null ? failure ?? "invalid-response" : "ok";
        metrics.CallDuration(request.Domain, localMode.ToString(), outcome, elapsedMs);
        logger.LogInformation(
            "policy_client_call domain={Domain} action={Action} mode={Mode} outcome={Outcome} elapsed_ms={ElapsedMs}",
            request.Domain,
            request.Action,
            localMode,
            outcome,
            (long)Math.Round(elapsedMs));

        if (reply is null)
            return OnFailure(request, localMode, failure ?? "invalid-response");

        var serverMode = reply.EffectiveMode!.Value;
        if (serverMode != localMode)
        {
            metrics.ModeMismatch(request.Domain);
            logger.LogWarning(
                "policy_mode_mismatch domain={Domain} local={LocalMode} server={ServerMode}",
                request.Domain,
                localMode,
                serverMode);
        }

        return serverMode switch
        {
            PolicyEnforcementMode.Enforce => reply,
            PolicyEnforcementMode.Shadow => ShadowResponse(request, reply),
            _ => OffResponse(request)
        };
    }

    private async Task<(PolicyEnforceResponse? Reply, string? Failure)> CallAsync(PolicyEnforceRequest request, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(EnforcePath, request, ct);
            if (response.StatusCode != HttpStatusCode.OK)
                return (null, $"http-{(int)response.StatusCode}");

            var reply = await response.Content.ReadFromJsonAsync<PolicyEnforceResponse>(JsonOptions, ct);
            return reply?.Decision is null || reply.EffectiveMode is null
                ? (null, "invalid-response")
                : (reply, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or TimeoutRejectedException)
        {
            logger.LogWarning(ex, "Policy enforce call timed out for {Domain}", request.Domain);
            return (null, "timeout");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Policy enforce call failed for {Domain}", request.Domain);
            return (null, "exception");
        }
    }

    private PolicyEnforceResponse OnFailure(PolicyEnforceRequest request, PolicyEnforcementMode localMode, string reason)
    {
        metrics.Failure(request.Domain, localMode.ToString(), reason);

        if (localMode == PolicyEnforcementMode.Shadow)
        {
            logger.LogWarning(
                "policy_client_failure domain={Domain} mode={Mode} reason={Reason}: Shadow does not block",
                request.Domain,
                localMode,
                reason);
            return OffResponse(request, PolicyEnforcementMode.Shadow, degraded: true, reason);
        }

        if (modeResolver.ResolveFailureMode(request.Domain) == PolicyFailureMode.AllowWithAlert)
        {
            logger.LogError(
                "policy_client_failure domain={Domain} mode={Mode} reason={Reason}: allowed by AllowWithAlert",
                request.Domain,
                localMode,
                reason);
            return OffResponse(request, localMode, degraded: true, reason);
        }

        logger.LogWarning(
            "policy_client_failure domain={Domain} mode={Mode} reason={Reason}: refused",
            request.Domain,
            localMode,
            reason);
        var message = $"تعذّر التحقق من سياسة {request.Domain} — لم يُنفَّذ الإجراء. أعد المحاولة أو راجع مسؤول النظام.";
        return new PolicyEnforceResponse
        {
            Allowed = false,
            Degraded = true,
            FailureReason = reason,
            EffectiveMode = localMode,
            Explanations = [message],
            Decision = new PolicyDecision { Allowed = false, DenyReason = message, Explanations = [message] }
        };
    }

    private static PolicyEnforceResponse ShadowResponse(PolicyEnforceRequest request, PolicyEnforceResponse reply)
    {
        var decision = PolicyStaticFallbackShim.Decide(request);
        return new PolicyEnforceResponse
        {
            Allowed = true,
            Effects = decision.Effects.Select(e => e.ToString()).ToList(),
            Explanations = decision.Explanations,
            Decision = decision,
            EffectiveMode = PolicyEnforcementMode.Shadow,
            ShadowDecision = reply.Decision,
            ShadowDiffs = reply.ShadowDiffs,
            LedgerId = reply.LedgerId,
            PolicyVersionId = reply.PolicyVersionId
        };
    }

    private static PolicyEnforceResponse OffResponse(
        PolicyEnforceRequest request,
        PolicyEnforcementMode mode = PolicyEnforcementMode.Off,
        bool degraded = false,
        string? failureReason = null)
    {
        var decision = PolicyStaticFallbackShim.Decide(request);
        return new PolicyEnforceResponse
        {
            Allowed = true,
            Effects = decision.Effects.Select(e => e.ToString()).ToList(),
            Explanations = decision.Explanations,
            Decision = decision,
            EffectiveMode = mode,
            Degraded = degraded,
            FailureReason = failureReason
        };
    }
}

/// <summary>The decision of the Off rule: the static custody rules for <c>Assets.Custody</c>, a neutral allow for the rest.</summary>
public static class PolicyStaticFallbackShim
{
    public static bool Supports(string domain) =>
        string.Equals(domain, PolicyDomains.AssetsCustody, StringComparison.OrdinalIgnoreCase);

    public static PolicyDecision Decide(PolicyEnforceRequest request) =>
        Supports(request.Domain)
            ? Evaluate(request)
            : new PolicyDecision { Allowed = true, Explanations = ["PolicyEngine:Off — neutral allow"] };

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
        services.AddHrPolicyEngineConfiguration();
        services.Configure<PolicyEngineValidationOptions>(options => options.UsesRemoteClient = true);
        services.TryAddSingleton<PolicyClientMetrics>();
        services.AddScoped<IPolicyEnforceClient, PolicyEnforceClient>();
        // PLAN-37: the HR.Core URL is required (start-up error naming Integration:HrCoreBaseUrl outside Development).
        services.AddServiceEndpointWarnings();
        services.AddServiceAuthenticatedClient(
            PolicyEnforceClient.HttpClientName,
            HrCoreEndpoint.RequireBaseUri(configuration),
            forwardActingUser: true);
        return services;
    }
}
