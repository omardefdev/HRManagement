using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
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

    public async Task<PolicyEnforceResponse> EnforceAsync(PolicyEnforceRequest request, CancellationToken ct = default)
    {
        var decision = await EvaluateAsync(request, ct);
        var shadowDiffs = new List<string>();

        if (request.Mode == PolicyEnforcementMode.Shadow)
        {
            shadowDiffs = PolicyShadowComparer.CompareWithStaticFallback(request, decision);
            logger.LogInformation("Policy shadow {Domain}/{Action}: diffs={Count}", request.Domain, request.Action, shadowDiffs.Count);
        }

        Guid? workflowInstanceId = null;
        if (request.Mode == PolicyEnforcementMode.Enforce
            && decision.Allowed
            && decision.Effects.Contains(PolicyEffect.TriggerWorkflow)
            && request.EntityId is Guid entityId)
        {
            workflowInstanceId = await TryStartWorkflowAsync(request, decision, entityId, ct);
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
            ShadowDiffJson = shadowDiffs.Count > 0 ? JsonSerializer.Serialize(shadowDiffs) : null,
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
            Explanations = decision.Explanations,
            PolicyVersionId = decision.PolicyVersionId,
            LedgerId = ledger.Id,
            Decision = decision,
            ShadowDiffs = shadowDiffs
        };
    }

    public async Task<PolicyDecision> EvaluateAsync(PolicyEnforceRequest request, CancellationToken ct = default)
    {
        var snapshot = await GetPublishedSnapshotAsync(request.Domain, request.TenantId, DateTime.UtcNow, ct);
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
        var asOf = asOfUtc ?? DateTime.UtcNow;
        var cacheKey = IPolicyPublishedSnapshotCache.BuildCacheKey(domain, tenantId, asOf);

        if (snapshotCache.TryGet(cacheKey, out PolicyVersionSnapshot? cached))
            return cached;

        var national = await LoadPublishedAsync(domain, PolicyTemplateScope.National, null, asOf, ct);
        PolicyVersionSnapshot? tenant = null;
        if (tenantId.HasValue)
            tenant = await LoadPublishedAsync(domain, PolicyTemplateScope.TenantOverride, tenantId, asOf, ct);

        var merged = PolicyEvaluator.MergeNationalAndTenant(national, tenant);
        if (merged.Rules.Count == 0 && national is null && tenant is null)
            return null;

        var schemaEntity = await db.PolicyFactsSchemas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Domain == domain, ct);
        var schema = PolicyEntityMapper.ParseSchema(schemaEntity);
        if (schema is not null)
            merged = merged with { FactsSchema = schema };

        snapshotCache.Set(cacheKey, merged, PolicyPublishedSnapshotCache.DefaultTtl);
        return merged;
    }

    private async Task<PolicyVersionSnapshot?> LoadPublishedAsync(
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

        if (template is null) return null;

        var version = template.Versions
            .Where(v => v.Status == PolicyVersionStatus.Published)
            .Where(v => v.EffectiveFrom == null || v.EffectiveFrom <= asOf)
            .Where(v => v.EffectiveTo == null || v.EffectiveTo >= asOf)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

        return version is null ? null : PolicyEntityMapper.ToSnapshot(template, version);
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
