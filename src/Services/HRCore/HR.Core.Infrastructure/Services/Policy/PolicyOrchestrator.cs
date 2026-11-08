using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.Policy;

public interface IPolicyOrchestrator
{
    Task<PolicyEnforceResponse> EnforceAsync(PolicyEnforceRequest request, CancellationToken ct = default);

    /// <summary>
    /// PLAN-42: the internal endpoint. The caller has already set mode, actor and tenant from the server side. A workflow starts
    /// only for a triple of <see cref="PolicyWorkflowTriggerAllowlist"/>, and the ledger names the client.
    /// </summary>
    Task<PolicyEnforceResponse> EnforceForServiceAsync(PolicyEnforceRequest request, string callerClientId, CancellationToken ct = default);

    Task<PolicyDecision> EvaluateAsync(PolicyEnforceRequest request, CancellationToken ct = default);
    Task<PolicyVersionSnapshot?> GetPublishedSnapshotAsync(string domain, Guid? tenantId, DateTime? asOfUtc = null, CancellationToken ct = default);
}

public sealed class PolicyOrchestrator(
    HRCoreDbContext db,
    IWorkflowStarter workflowStarter,
    IPolicyPublishedSnapshotCache snapshotCache,
    ILogger<PolicyOrchestrator> logger) : IPolicyOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<PolicyEnforceResponse> EnforceAsync(PolicyEnforceRequest request, CancellationToken ct = default) =>
        EnforceCoreAsync(request, callerClientId: null, ct);

    public Task<PolicyEnforceResponse> EnforceForServiceAsync(PolicyEnforceRequest request, string callerClientId, CancellationToken ct = default) =>
        EnforceCoreAsync(request, callerClientId, ct);

    private async Task<PolicyEnforceResponse> EnforceCoreAsync(PolicyEnforceRequest request, string? callerClientId, CancellationToken ct)
    {
        // PLAN-42: Off means the engine has no say here: an allow with no ledger row and no workflow.
        if (request.Mode == PolicyEnforcementMode.Off)
        {
            return new PolicyEnforceResponse
            {
                Allowed = true,
                EffectiveMode = PolicyEnforcementMode.Off,
                Explanations = ["PolicyEngine:Off"]
            };
        }

        var decision = await EvaluateAsync(request, ct);
        var shadowDiffs = new List<string>();

        if (request.Mode == PolicyEnforcementMode.Shadow)
        {
            shadowDiffs = PolicyShadowComparer.CompareWithStaticFallback(request, decision);
            logger.LogInformation("Policy shadow {Domain}/{Action}: diffs={Count}", request.Domain, request.Action, shadowDiffs.Count);
        }

        Guid? workflowInstanceId = null;
        var explanations = decision.Explanations;
        if (request.Mode == PolicyEnforcementMode.Enforce
            && decision.Allowed
            && decision.Effects.Contains(PolicyEffect.TriggerWorkflow))
        {
            var mayStart = callerClientId is null
                || PolicyWorkflowTriggerAllowlist.Allows(request.Domain, request.SubjectType, decision.GetWorkflowCode());
            if (mayStart && request.EntityId is Guid entityId && entityId != Guid.Empty)
                workflowInstanceId = await TryStartWorkflowAsync(request, decision, entityId, ct);
            else if (callerClientId is not null)
                explanations = [.. decision.Explanations, PolicyWorkflowTriggerAllowlist.NotAllowedExplanation];
        }

        var ledger = new PolicyEnforcementLedger
        {
            Domain = request.Domain,
            Action = request.Action,
            PolicyVersionId = decision.PolicyVersionId,
            CorrelationId = request.CorrelationId,
            FactsHash = HashFacts(request.Facts),
            DecisionJson = JsonSerializer.Serialize(decision, JsonOptions),
            ActorUserId = request.ActorUserId,
            ActorUserName = request.ActorUserName,
            Mode = (PolicyEnforcementModeValue)request.Mode,
            WorkflowInstanceId = workflowInstanceId,
            CallerClientId = callerClientId,
            SubjectType = request.SubjectType,
            EntityId = request.EntityId,
            ShadowDiffJson = shadowDiffs.Count > 0 ? JsonSerializer.Serialize(shadowDiffs) : null,
            // PLAN-47: recorded only when the decision was taken as of another time than the request's.
            EvaluatedAsOf = request.AsOfUtc,
            OccurredAt = DateTime.UtcNow
        };
        db.PolicyEnforcementLedgers.Add(ledger);
        await db.SaveChangesAsync(ct);

        var effectiveAllowed = request.Mode == PolicyEnforcementMode.Shadow
            ? true
            : decision.Allowed;

        return new PolicyEnforceResponse
        {
            Allowed = effectiveAllowed,
            Effects = decision.Effects.Select(e => e.ToString()).ToList(),
            WorkflowInstanceId = request.Mode == PolicyEnforcementMode.Enforce ? workflowInstanceId : null,
            Explanations = explanations,
            PolicyVersionId = decision.PolicyVersionId,
            LedgerId = ledger.Id,
            Decision = decision,
            ShadowDiffs = shadowDiffs,
            EffectiveMode = request.Mode
        };
    }

    /// <summary>PLAN-47 (§6): the published policy now, or as of <see cref="PolicyEnforceRequest.AsOfUtc"/>.</summary>
    public async Task<PolicyDecision> EvaluateAsync(PolicyEnforceRequest request, CancellationToken ct = default)
    {
        var snapshot = await GetPublishedSnapshotAsync(request.Domain, request.TenantId, request.AsOfUtc, ct);
        if (snapshot is null || snapshot.Rules.Count == 0)
        {
            return PolicyStaticFallback.Evaluate(request);
        }

        return PolicyEvaluator.Evaluate(snapshot, request.Facts, request.Action);
    }

    public async Task<PolicyVersionSnapshot?> GetPublishedSnapshotAsync(
        string domain,
        Guid? tenantId,
        DateTime? asOfUtc = null,
        CancellationToken ct = default)
    {
        // PLAN-47 (§6): only "now" is cached. An evaluation as of a date reads the database; the "now" entry lives at most 5
        // minutes, or until the next EffectiveFrom of the domain's templates, whichever comes first (no stale version across a
        // boundary). Publications clear it in every instance (PolicyPublishedCacheInvalidationConsumer).
        var now = DateTime.UtcNow;
        var asOf = asOfUtc ?? now;
        var cacheKey = asOfUtc is null ? IPolicyPublishedSnapshotCache.BuildCacheKey(domain, tenantId) : null;

        if (cacheKey is not null && snapshotCache.TryGet(cacheKey, out PolicyVersionSnapshot? cached))
            return cached;

        var (national, nationalNext) = await LoadPublishedAsync(domain, PolicyTemplateScope.National, null, asOf, ct);
        PolicyVersionSnapshot? tenant = null;
        DateTime? tenantNext = null;
        if (tenantId.HasValue)
            (tenant, tenantNext) = await LoadPublishedAsync(domain, PolicyTemplateScope.TenantOverride, tenantId, asOf, ct);

        var next = nationalNext is null ? tenantNext : tenantNext is null ? nationalNext : (nationalNext < tenantNext ? nationalNext : tenantNext);
        // Nothing effective (yet): not cached, so the first version is seen the moment it takes effect.
        if (national is null && tenant is null)
            return null;

        var merged = PolicyEvaluator.MergeNationalAndTenant(national, tenant) with { NextEffectiveFrom = next };

        var schemaEntity = await db.PolicyFactsSchemas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Domain == domain, ct);
        var schema = PolicyEntityMapper.ParseSchema(schemaEntity);
        if (schema is not null)
            merged = merged with { FactsSchema = schema };

        // PLAN-47: a stored rule the strict parser refuses matches nothing; say so once per load of the snapshot.
        if (merged.InvalidRuleKeys.Count > 0)
            logger.LogError(
                "PLAN-47 policy version {VersionId} holds {Count} rule(s) the strict parser refuses; they never match: {RuleKeys}",
                merged.VersionId, merged.InvalidRuleKeys.Count, string.Join(",", merged.InvalidRuleKeys));

        if (cacheKey is not null)
        {
            var ttl = PolicyPublishedSnapshotCache.TimeToLive(now, merged.NextEffectiveFrom);
            if (ttl > TimeSpan.Zero)
                snapshotCache.Set(cacheKey, merged, ttl);
        }

        return merged;
    }

    /// <summary>The template's version effective at <paramref name="asOf"/>, and the next EffectiveFrom after it.</summary>
    private async Task<(PolicyVersionSnapshot? Snapshot, DateTime? Next)> LoadPublishedAsync(
        string domain,
        PolicyTemplateScope scope,
        Guid? tenantId,
        DateTime asOf,
        CancellationToken ct)
    {
        var template = await db.PolicyTemplates.AsNoTracking()
            .Include(t => t.Versions.Where(v => v.Status == PolicyVersionStatus.Published))
            .ThenInclude(v => v.Rules)
            .FirstOrDefaultAsync(t =>
                t.Domain == domain
                && t.Scope == scope
                && (scope == PolicyTemplateScope.National || t.TenantId == tenantId), ct);

        if (template is null) return (null, null);

        // PLAN-47: the effective window is [EffectiveFrom, EffectiveTo), the same choice PolicyDraftService.EffectiveVersion makes.
        var version = template.Versions
            .Where(v => v.Status == PolicyVersionStatus.Published)
            .Where(v => v.EffectiveFrom == null || v.EffectiveFrom <= asOf)
            .Where(v => v.EffectiveTo == null || v.EffectiveTo > asOf)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

        var next = template.Versions
            .Where(v => v.Status == PolicyVersionStatus.Published && v.EffectiveFrom > asOf)
            .Select(v => v.EffectiveFrom)
            .Min();

        return (version is null ? null : PolicyEntityMapper.ToSnapshot(template, version), next);
    }

    private async Task<Guid?> TryStartWorkflowAsync(
        PolicyEnforceRequest request,
        PolicyDecision decision,
        Guid entityId,
        CancellationToken ct)
    {
        var workflowCode = decision.GetWorkflowCode();
        if (string.IsNullOrWhiteSpace(workflowCode))
            return null;

        // PLAN-43 (rule 10): a registered type starts only at its owner. The decision keeps its TriggerWorkflow effect, and no
        // instance takes its actor from a policy request.
        if (ExternalApprovalTypes.IsRegistered(request.SubjectType))
            return null;

        return await workflowStarter.StartByWorkflowCodeAsync(
            workflowCode,
            request.SubjectType,
            entityId,
            request.TenantId,
            request.ActorUserId,
            request.ActorUserName,
            request.ContextJson,
            request.Domain,
            ct);
    }

    private static string HashFacts(IReadOnlyDictionary<string, string> facts)
    {
        var canonical = string.Join("|", facts.OrderBy(k => k.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash)[..16];
    }
}
