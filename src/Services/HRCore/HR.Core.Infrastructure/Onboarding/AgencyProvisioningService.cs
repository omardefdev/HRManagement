using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Entities.Seeding;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Seeding;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HR.Core.Infrastructure.Onboarding;

public sealed class AgencyProvisioningService
{
    private readonly HRCoreDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly AgencyOrgStructureProvisioner _orgProvisioner;
    private readonly IPublishEndpoint _publish;
    private readonly ILogger<AgencyProvisioningService> _logger;

    public AgencyProvisioningService(
        HRCoreDbContext db,
        IServiceProvider serviceProvider,
        AgencyOrgStructureProvisioner orgProvisioner,
        IPublishEndpoint publish,
        ILogger<AgencyProvisioningService> logger)
    {
        _db = db;
        _serviceProvider = serviceProvider;
        _orgProvisioner = orgProvisioner;
        _publish = publish;
        _logger = logger;
    }

    public async Task<AgencyProvisioningResult> ApplyManifestAsync(
        AgencyManifest manifest,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        AgencyManifestLoader.Validate(manifest);
        var steps = new List<string>();
        var warnings = new List<string>();

        var tenantCode = manifest.AgencyId.Length <= 50
            ? manifest.AgencyId.ToUpperInvariant()
            : manifest.AgencyId[..50].ToUpperInvariant();

        var existingTenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Code == tenantCode, ct);
        if (existingTenant is not null && !dryRun)
        {
            var alreadyApplied = await _db.SeedPackApplications
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.TenantProvisioning)
                .AnyAsync(
                p => p.TenantId == existingTenant.Id
                     && p.PackId == manifest.PackId
                     && p.ReleaseDigest == manifest.ReleaseDigest, ct);
            if (alreadyApplied)
            {
                _logger.LogInformation(
                    "ONB-001 manifest {PackId} already applied for agency {AgencyId}",
                    manifest.PackId, manifest.AgencyId);

                return new AgencyProvisioningResult
                {
                    DryRun = false,
                    AlreadyApplied = true,
                    TenantId = existingTenant.Id,
                    AgencyId = manifest.AgencyId,
                    PackId = manifest.PackId,
                    Steps = ["AlreadyApplied"],
                    Warnings = ["AlreadyApplied"]
                };
            }
        }

        steps.Add("EnsureNationalMandatory");
        if (!dryRun)
            await NationalSeedOrchestrator.ApplyMandatoryAsync(_db, _serviceProvider, ct);

        // PLAN-40 (class B): the tenant, its org units, packs, the recorded application and every message they publish
        // (IOrgUnitChanged, IAgencyOnboarded) commit in one transaction. The national seed above is shared and idempotent.
        return dryRun
            ? await ApplyAgencyAsync(manifest, existingTenant, tenantCode, steps, warnings, dryRun, ct)
            : await OutboxUnitOfWork.ExecuteAsync(
                _db, token => ApplyAgencyAsync(manifest, existingTenant, tenantCode, steps, warnings, dryRun, token), ct);
    }

    private async Task<AgencyProvisioningResult> ApplyAgencyAsync(
        AgencyManifest manifest,
        Tenant? existingTenant,
        string tenantCode,
        List<string> steps,
        List<string> warnings,
        bool dryRun,
        CancellationToken ct)
    {
        Guid tenantId;
        if (existingTenant is null)
        {
            tenantId = Guid.NewGuid();
            steps.Add("CreateTenant");
            if (!dryRun)
            {
                _db.Tenants.Add(new Tenant
                {
                    Id = tenantId,
                    Code = tenantCode,
                    NameAr = manifest.Tenant.DisplayName,
                    NameEn = manifest.Tenant.DisplayName,
                    IsActive = true
                });
                await _db.SaveChangesAsync(ct);
            }
        }
        else
        {
            tenantId = existingTenant.Id;
            steps.Add("ReuseExistingTenant");
            warnings.Add($"Tenant code {tenantCode} already exists");
        }

        IReadOnlyList<string> orgUnitCodes = Array.Empty<string>();
        if (manifest.OrgStructure.Units.Count > 0)
        {
            steps.Add("ApplyOrgStructure");
            if (!dryRun)
            {
                orgUnitCodes = await _orgProvisioner.ApplyAsync(
                    tenantId, manifest.OrgStructure, warnings, ct);
            }
        }

        steps.Add("ApplyReferenceSubset");
        if (!dryRun)
        {
            await MdmReferenceSeeder.CopySubsetAsync(
                _db,
                TenantDefaults.DefaultTenantId,
                tenantId,
                manifest.ReferenceSubset.CatalogKinds,
                ct);
        }

        foreach (var packId in manifest.Policies.PolicyPackIds)
        {
            steps.Add($"ApplyPolicyPack:{packId}");
            if (!dryRun)
            {
                var pack = await AgencyPolicySeeder.ApplyPackAsync(_db, _serviceProvider, tenantId, packId, ct);
                warnings.AddRange(pack.Skipped.Select(s => $"Policy overlay {packId}: {s}"));
            }
        }

        foreach (var packId in manifest.Policies.CodePackIds)
        {
            steps.Add($"ApplyCodePack:{packId}");
            if (!dryRun)
                await AgencyCodePolicySeeder.ApplyPackAsync(_db, tenantId, packId, ct);
        }

        foreach (var packId in manifest.Workflows.WorkflowPackIds)
        {
            steps.Add($"ApplyWorkflowPack:{packId}");
            if (!dryRun)
                warnings.Add($"Workflow overlay pack {packId} registered (national workflows already seeded)");
        }

        steps.Add("RecordSeedPackApplication");
        if (!dryRun)
        {
            _db.SeedPackApplications.Add(new SeedPackApplication
            {
                TenantId = tenantId,
                PackId = manifest.PackId,
                ReleaseDigest = manifest.ReleaseDigest,
                RollbackRef = manifest.RollbackRef,
                AppliedAt = DateTime.UtcNow
            });

            // PLAN-40 (class A): published before the save that records the application, so both commit together.
            await _publish.Publish<IAgencyOnboarded>(new
            {
                TenantId = tenantId,
                AgencyId = manifest.AgencyId,
                PackId = manifest.PackId,
                ReleaseDigest = manifest.ReleaseDigest,
                OrgUnitCodes = orgUnitCodes,
                OnboardedAt = DateTime.UtcNow,
                CorrelationId = (string?)null
            }, ct);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("ONB-001 manifest {PackId} applied for agency {AgencyId} dryRun={DryRun}",
            manifest.PackId, manifest.AgencyId, dryRun);

        return new AgencyProvisioningResult
        {
            DryRun = dryRun,
            TenantId = dryRun ? null : tenantId,
            AgencyId = manifest.AgencyId,
            PackId = manifest.PackId,
            Steps = steps,
            Warnings = warnings
        };
    }
}

/// <summary>PLAN-48 (P-6): an overlay pack the validator or the ceiling refuses, refused whole before anything is saved.</summary>
public sealed class AgencyPolicyPackException(string packId, IReadOnlyList<string> issues)
    : InvalidOperationException($"Policy overlay {packId} refused: {string.Join(" | ", issues)}")
{
    public string PackId { get; } = packId;

    public IReadOnlyList<string> Issues { get; } = issues;
}

/// <summary>What <see cref="AgencyPolicySeeder.ApplyPackAsync"/> did, per domain.</summary>
public sealed record AgencyPolicyPackResult(string PackId, IReadOnlyList<string> Published, IReadOnlyList<string> Skipped);

/// <summary>
/// Applies tenant-scoped policy overlay packs from seed-packs/policy-overlays/. PLAN-48 (§4, P-6): for each domain of the pack, one
/// draft of the agency's override (a copy of its version in force, plus every rule of the pack not in it yet), then the added
/// rules validated with the agency ceiling (D-TEN-8 = D-POL-4), then a SystemSeed publication through
/// <see cref="PolicyPublishService"/> (no four eyes, the pack id as evidence). A pack with one refused rule is refused whole,
/// before anything is saved. A stored key is "{ruleKeyPrefix}:{ruleKey}" (PLAN-47's key pattern); a rule renamed in its pack
/// keeps its old key in "renamedFrom", so an agency provisioned before the rename does not get the rule twice.
/// <para>
/// Until PLAN-48 the pack was read case-sensitively (its camelCase names never bound), so no overlay rule was ever added; and
/// a domain received its first rule only.
/// </para>
/// </summary>
public static class AgencyPolicySeeder
{
    private sealed class PolicyOverlayPack
    {
        public string PackId { get; set; } = "";

        /// <summary>PLAN-48: the prefix of the stored keys; the pack id when absent (it must then fit the key pattern).</summary>
        public string? RuleKeyPrefix { get; set; }

        public List<PolicyOverlayRule> Rules { get; set; } = [];
    }

    private sealed class PolicyOverlayRule
    {
        public string Domain { get; set; } = "";
        public string RuleKey { get; set; } = "";
        public string Name { get; set; } = "";
        public int Priority { get; set; } = 10;
        public string WhenJson { get; set; } = "";
        public string ThenJson { get; set; } = "";

        /// <summary>PLAN-48: the stored key the rule had before its pack was renamed (recorded; never stored again).</summary>
        public string? RenamedFrom { get; set; }
    }

    private static readonly JsonSerializerOptions PackJson = new() { PropertyNameCaseInsensitive = true };

    public static async Task<AgencyPolicyPackResult> ApplyPackAsync(
        HRCoreDbContext db,
        IServiceProvider? services,
        Guid tenantId,
        string packId,
        CancellationToken ct = default)
    {
        var path = ResolveOverlayPath("policy-overlays", packId);
        if (path is null || !File.Exists(path))
            return new AgencyPolicyPackResult(packId, [], [$"{packId}: no pack file"]);

        return await ApplyPackContentAsync(db, services, tenantId, packId, await File.ReadAllTextAsync(path, ct), ct);
    }

    /// <summary>The same, for a pack's JSON (tools and tests); <paramref name="packId"/> must be the id the pack names.</summary>
    public static async Task<AgencyPolicyPackResult> ApplyPackContentAsync(
        HRCoreDbContext db,
        IServiceProvider? services,
        Guid tenantId,
        string packId,
        string packJson,
        CancellationToken ct = default)
    {
        var pack = JsonSerializer.Deserialize<PolicyOverlayPack>(packJson, PackJson)
                   ?? throw new InvalidOperationException($"Invalid policy overlay: {packId}");
        if (!string.Equals(pack.PackId, packId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Policy overlay '{packId}' names pack '{pack.PackId}'");

        var prefix = string.IsNullOrWhiteSpace(pack.RuleKeyPrefix) ? pack.PackId : pack.RuleKeyPrefix.Trim();
        var (validation, publisher) = CreateServices(db, services);
        var now = DateTime.UtcNow;

        // 1. Every domain planned and every added rule validated (with the ceiling) before anything is saved.
        var plans = new List<(string Domain, PolicyTemplate? Template, PolicyTemplateVersion? Effective, List<PolicyOverlayRule> Added)>();
        var issues = new List<string>();
        var skipped = new List<string>();
        foreach (var group in pack.Rules.GroupBy(r => r.Domain, StringComparer.Ordinal))
        {
            // The agency's own override, named explicitly: the provisioning session is not the agency's.
            var template = await db.PolicyTemplates.OwnOrNational(tenantId)
                .Include(t => t.Versions).ThenInclude(v => v.Rules)
                .Where(t => t.TenantId == tenantId && t.Domain == group.Key && t.Scope == PolicyTemplateScope.TenantOverride)
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .FirstOrDefaultAsync(ct);
            var effective = template is null ? null : PolicyDraftService.EffectiveVersion(template, now);
            var present = new HashSet<string>(effective?.Rules.Select(r => r.RuleKey ?? string.Empty) ?? [], StringComparer.Ordinal);
            var added = group
                .Where(r => !present.Contains($"{prefix}:{r.RuleKey}") && (r.RenamedFrom is null || !present.Contains(r.RenamedFrom)))
                .ToList();
            if (added.Count == 0)
            {
                skipped.Add($"{group.Key}: every rule already in force");
                continue;
            }

            if (template?.Versions.Any(v => v.Status == PolicyVersionStatus.Draft) == true)
            {
                issues.Add($"{group.Key}: the agency's override has an open draft (one draft per template)");
                continue;
            }

            var found = await validation.ValidateVersionAsync(
                group.Key,
                PolicyTemplateScope.TenantOverride,
                added.Select(r => new PolicyRuleInput($"{prefix}:{r.RuleKey}", r.Name, r.Priority, r.WhenJson, r.ThenJson)).ToList(),
                ct);
            issues.AddRange(found.Select(i => $"{group.Key} {i.JsonPath}: {i.MessageAr} ({i.Code})"));
            plans.Add((group.Key, template, effective, added));
        }

        if (issues.Count > 0)
            throw new AgencyPolicyPackException(packId, issues);

        // 2. One draft per domain, published as a system seed through the one publish path.
        var published = new List<string>();
        foreach (var (domain, existing, effective, added) in plans)
        {
            var template = existing;
            if (template is null)
            {
                template = new PolicyTemplate
                {
                    TenantId = tenantId,
                    Domain = domain,
                    Name = $"overlay — {packId}",
                    SubjectType = "AgencyOverlay",
                    Scope = PolicyTemplateScope.TenantOverride,
                    Description = $"ONB policy overlay {packId}"
                };
                db.PolicyTemplates.Add(template);
            }

            var draft = new PolicyTemplateVersion
            {
                PolicyTemplate = template,
                PolicyTemplateId = template.Id,
                TenantId = tenantId,
                VersionNumber = (template.Versions.Count == 0 ? 0 : template.Versions.Max(v => v.VersionNumber)) + 1,
                Status = PolicyVersionStatus.Draft,
                ChangeReason = $"overlay {packId}"
            };
            db.PolicyTemplateVersions.Add(draft);
            db.PolicyVersionEdits.Add(new PolicyVersionEdit
            {
                PolicyTemplateVersionId = draft.Id,
                TenantId = tenantId,
                Action = effective is null ? PolicyVersionEditAction.Create : PolicyVersionEditAction.Copy,
                OccurredAt = now
            });

            foreach (var rule in effective?.Rules.OrderBy(r => r.Priority).ThenBy(r => r.RuleKey, StringComparer.Ordinal) ?? Enumerable.Empty<PolicyRule>())
            {
                db.PolicyRules.Add(new PolicyRule
                {
                    PolicyTemplateVersionId = draft.Id,
                    TenantId = tenantId,
                    RuleKey = rule.RuleKey,
                    Name = rule.Name,
                    Priority = rule.Priority,
                    WhenJson = rule.WhenJson,
                    ThenJson = rule.ThenJson
                });
            }

            var addedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in added)
            {
                var key = $"{prefix}:{rule.RuleKey}";
                addedKeys.Add(key);
                db.PolicyRules.Add(new PolicyRule
                {
                    PolicyTemplateVersionId = draft.Id,
                    TenantId = tenantId,
                    RuleKey = key,
                    Name = rule.Name,
                    Priority = rule.Priority,
                    WhenJson = rule.WhenJson,
                    ThenJson = rule.ThenJson
                });
                db.PolicyVersionEdits.Add(new PolicyVersionEdit
                {
                    PolicyTemplateVersionId = draft.Id,
                    TenantId = tenantId,
                    Action = PolicyVersionEditAction.AddRule,
                    RuleKey = key,
                    OccurredAt = now
                });
            }

            await db.SaveChangesAsync(ct);
            // The draft's rules as saved, for the publish service's validation and the publication.
            await db.Entry(draft).Collection(v => v.Rules).LoadAsync(ct);

            var result = await publisher.PublishSystemSeedAsync(draft, $"overlay-{packId}", addedKeys, ct);
            if (!result.Succeeded)
                throw new AgencyPolicyPackException(packId,
                    [$"{domain}: {result.MessageAr}", .. result.IssueList.Select(i => $"{domain} {i.JsonPath}: {i.MessageAr} ({i.Code})")]);
            published.Add($"{domain}: v{draft.VersionNumber} ({string.Join(", ", addedKeys)})");
        }

        return new AgencyPolicyPackResult(packId, published, skipped);
    }

    /// <summary>The request's validation and publish services when they hold the same context, otherwise ones built over it.</summary>
    private static (IPolicyRuleValidationService Validation, PolicyPublishService Publisher) CreateServices(HRCoreDbContext db, IServiceProvider? services)
    {
        if (services?.GetService<HRCoreDbContext>() is { } scoped && ReferenceEquals(scoped, db)
            && services.GetService<IPolicyRuleValidationService>() is { } scopedValidation
            && services.GetService<PolicyPublishService>() is { } scopedPublisher)
            return (scopedValidation, scopedPublisher);

        // No service container over this context (tests, tools): the same services, without the bus (no IPolicyPublished).
        var validation = PolicyRuleValidationService.ForCatalog(db);
        var cache = services?.GetService<IPolicyPublishedSnapshotCache>()
                    ?? new PolicyPublishedSnapshotCache(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        return (validation, new PolicyPublishService(db, validation, new PolicyVersionPublisher(db, cache, services?.GetService<IPublishEndpoint>())));
    }

    private static string? ResolveOverlayPath(string folder, string packId) =>
        SeedPackPathResolver.FindPackFile(Path.Combine(folder, $"{packId}.json"));
}

/// <summary>
/// Applies tenant-scoped code generation overlay packs. PLAN-48 (§2, §3): a policy marked <c>"default": true</c> replaces the
/// kind's default template in the agency (it is stored with no policy key, so a request without a key gets it); every policy is
/// validated (<see cref="CodePolicyValidator"/>: one sequence, its scope shown in the number, a yearly reset shows the year, one
/// default overlay per kind) before anything is saved, and an invalid pack is refused whole.
/// </summary>
public static class AgencyCodePolicySeeder
{
    private sealed class CodeOverlayPack
    {
        public string PackId { get; set; } = "";
        public List<CodeOverlayPolicy> Policies { get; set; } = [];
    }

    private sealed class CodeOverlayPolicy
    {
        public string PolicyKey { get; set; } = "";
        public string EntityKind { get; set; } = "";
        public string NameAr { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string SegmentsJson { get; set; } = "[]";

        /// <summary>PLAN-48: the overlay replaces the kind's default template in this agency.</summary>
        public bool Default { get; set; }

        /// <summary>PLAN-48: the counter reset (Never, Yearly, PerScope); a pack states it, the entity default is Yearly.</summary>
        public string? SequenceReset { get; set; }
    }

    private static readonly JsonSerializerOptions PackJson = new() { PropertyNameCaseInsensitive = true };

    public static async Task ApplyPackAsync(
        HRCoreDbContext db,
        Guid tenantId,
        string packId,
        CancellationToken ct = default)
    {
        var path = ResolveOverlayPath(packId);
        if (path is null || !File.Exists(path))
            return;

        var pack = JsonSerializer.Deserialize<CodeOverlayPack>(await File.ReadAllTextAsync(path, ct), PackJson)
                   ?? throw new InvalidOperationException($"Invalid code overlay: {path}");

        // The agency's overlays, read with an explicit tenant predicate (the caller's ambient tenant may be none).
        var existing = await db.CodePolicyTemplates.OwnOrNational(tenantId).AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Scope == CodePolicyScope.TenantOverride)
            .Select(t => new CodePolicyTemplateKey(t.TenantId, t.EntityKind, t.Scope, t.PolicyKey))
            .ToListAsync(ct);

        var planned = new List<(CodePolicyTemplate Template, CodePolicyTemplateKey Key)>();
        var issues = new List<string>();
        foreach (var policy in pack.Policies)
        {
            if (!Enum.TryParse<CodeEntityKind>(policy.EntityKind, ignoreCase: true, out var entityKind))
            {
                issues.Add($"{policy.PolicyKey}: unknown kind {policy.EntityKind}");
                continue;
            }

            var reset = CodeSequenceReset.Yearly;
            if (!string.IsNullOrWhiteSpace(policy.SequenceReset) && !Enum.TryParse(policy.SequenceReset, ignoreCase: true, out reset))
            {
                issues.Add($"{policy.PolicyKey}: unknown sequence reset {policy.SequenceReset}");
                continue;
            }

            foreach (var issue in CodePolicyValidator.ValidateVersion(entityKind, policy.SegmentsJson, reset))
                issues.Add($"{policy.PolicyKey}: {issue.Code} {issue.MessageAr}");

            var storedKey = policy.Default ? null : policy.PolicyKey;
            var key = new CodePolicyTemplateKey(tenantId, entityKind, CodePolicyScope.TenantOverride, storedKey);
            if (existing.Contains(key))
                continue;

            var template = new CodePolicyTemplate
            {
                TenantId = tenantId,
                PolicyKey = storedKey,
                EntityKind = entityKind,
                Name = policy.NameAr,
                Scope = CodePolicyScope.TenantOverride,
                Description = policy.Default
                    ? $"ONB code overlay {packId} ({policy.PolicyKey}, the agency's default for {entityKind})"
                    : $"ONB code overlay {packId}"
            };
            template.Versions.Add(new CodePolicyVersion
            {
                TenantId = tenantId,
                VersionNumber = 1,
                Status = CodePolicyVersionStatus.Published,
                EffectiveFrom = DateTime.UtcNow,
                SegmentsJson = policy.SegmentsJson,
                SequenceReset = reset,
                FallbackPrefix = policy.Prefix
            });
            planned.Add((template, key));
        }

        foreach (var issue in CodePolicyValidator.ValidateDefaults(existing.Concat(planned.Select(p => p.Key))))
            issues.Add(issue.Code + " " + issue.MessageAr);
        if (issues.Count > 0)
            throw new InvalidOperationException($"PLAN-48: code overlay pack {packId} is refused: " + string.Join("; ", issues));

        if (planned.Count == 0)
            return;

        db.CodePolicyTemplates.AddRange(planned.Select(p => p.Template));
        await db.SaveChangesAsync(ct);
        CodeGeneratorService.InvalidateSnapshotCache();
    }

    private static string? ResolveOverlayPath(string packId) =>
        SeedPackPathResolver.FindPackFile(Path.Combine("code-overlays", $"{packId}.json"));
}
