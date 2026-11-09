using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Entities.Seeding;
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
                await AgencyPolicySeeder.ApplyPackAsync(_db, tenantId, packId, ct);
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

/// <summary>Applies tenant-scoped policy overlay packs from seed-packs/policy-overlays/.</summary>
public static class AgencyPolicySeeder
{
    private sealed class PolicyOverlayPack
    {
        public string PackId { get; set; } = "";
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
    }

    public static async Task ApplyPackAsync(
        HRCoreDbContext db,
        Guid tenantId,
        string packId,
        CancellationToken ct = default)
    {
        var path = ResolveOverlayPath("policy-overlays", packId);
        if (path is null || !File.Exists(path))
            return;

        var pack = JsonSerializer.Deserialize<PolicyOverlayPack>(await File.ReadAllTextAsync(path, ct))
                   ?? throw new InvalidOperationException($"Invalid policy overlay: {path}");

        foreach (var rule in pack.Rules)
        {
            if (await db.PolicyRules.AnyAsync(r => r.RuleKey == $"{packId}:{rule.RuleKey}", ct))
                continue;

            var template = await db.PolicyTemplates.FirstOrDefaultAsync(
                t => t.TenantId == tenantId && t.Domain == rule.Domain && t.Scope == PolicyTemplateScope.TenantOverride, ct);

            if (template is null)
            {
                template = new PolicyTemplate
                {
                    TenantId = tenantId,
                    Domain = rule.Domain,
                    Name = $"overlay — {packId}",
                    SubjectType = "AgencyOverlay",
                    Scope = PolicyTemplateScope.TenantOverride,
                    Description = $"ONB policy overlay {packId}"
                };
                db.PolicyTemplates.Add(template);

                // PLAN-47: rules go only to a draft; the version is added as a draft, then published (the overlay's own record).
                await PolicySeedVersionWriter.AddAsync(db, new PolicyTemplateVersion
                {
                    PolicyTemplate = template,
                    TenantId = tenantId,
                    VersionNumber = 1,
                    EffectiveFrom = DateTime.UtcNow
                },
                [
                    new PolicyRule
                    {
                        TenantId = tenantId,
                        RuleKey = $"{packId}:{rule.RuleKey}",
                        Name = rule.Name,
                        Priority = rule.Priority,
                        WhenJson = rule.WhenJson,
                        ThenJson = rule.ThenJson
                    }
                ], PolicyVersionStatus.Published, new PolicyPublication
                {
                    TenantId = tenantId,
                    PublishedAt = DateTime.UtcNow,
                    EvidenceRef = $"overlay-{packId}",
                    Kind = PolicyPublicationKind.SystemSeed,
                    MakerRole = PolicyPublishService.SystemRoleName,
                    CheckerRole = PolicyPublishService.SystemRoleName
                }, ct);
            }
        }

        await db.SaveChangesAsync(ct);
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
