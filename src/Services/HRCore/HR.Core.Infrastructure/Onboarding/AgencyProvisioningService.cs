using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Entities.Seeding;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
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
            await _db.SaveChangesAsync(ct);

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

                var version = new PolicyTemplateVersion
                {
                    PolicyTemplate = template,
                    VersionNumber = 1,
                    Status = PolicyVersionStatus.Published,
                    EffectiveFrom = DateTime.UtcNow
                };
                db.PolicyTemplateVersions.Add(version);

                db.PolicyRules.Add(new PolicyRule
                {
                    PolicyTemplateVersion = version,
                    RuleKey = $"{packId}:{rule.RuleKey}",
                    Name = rule.Name,
                    Priority = rule.Priority,
                    WhenJson = rule.WhenJson,
                    ThenJson = rule.ThenJson
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? ResolveOverlayPath(string folder, string packId) =>
        SeedPackPathResolver.FindPackFile(Path.Combine(folder, $"{packId}.json"));
}

/// <summary>Applies tenant-scoped code generation overlay packs.</summary>
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
    }

    public static async Task ApplyPackAsync(
        HRCoreDbContext db,
        Guid tenantId,
        string packId,
        CancellationToken ct = default)
    {
        var path = ResolveOverlayPath(packId);
        if (path is null || !File.Exists(path))
            return;

        var pack = JsonSerializer.Deserialize<CodeOverlayPack>(await File.ReadAllTextAsync(path, ct))
                   ?? throw new InvalidOperationException($"Invalid code overlay: {path}");

        foreach (var policy in pack.Policies)
        {
            if (!Enum.TryParse<CodeEntityKind>(policy.EntityKind, ignoreCase: true, out var entityKind))
                continue;

            if (await db.CodePolicyTemplates.AnyAsync(
                    t => t.TenantId == tenantId && t.PolicyKey == policy.PolicyKey, ct))
                continue;

            var template = new CodePolicyTemplate
            {
                TenantId = tenantId,
                PolicyKey = policy.PolicyKey,
                EntityKind = entityKind,
                Name = policy.NameAr,
                Scope = CodePolicyScope.TenantOverride,
                Description = $"ONB code overlay {packId}"
            };

            var version = new CodePolicyVersion
            {
                TenantId = tenantId,
                VersionNumber = 1,
                Status = CodePolicyVersionStatus.Published,
                EffectiveFrom = DateTime.UtcNow,
                SegmentsJson = policy.SegmentsJson,
                FallbackPrefix = policy.Prefix
            };

            template.Versions.Add(version);
            db.CodePolicyTemplates.Add(template);
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? ResolveOverlayPath(string packId) =>
        SeedPackPathResolver.FindPackFile(Path.Combine("code-overlays", $"{packId}.json"));
}
