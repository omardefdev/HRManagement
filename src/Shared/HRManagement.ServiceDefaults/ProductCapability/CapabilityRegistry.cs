using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ProductCapability;

public interface ICapabilityRegistry
{
    CapabilityMapDocument CapabilityMap { get; }
    AgencyArchetypesDocument Archetypes { get; }
    bool IsModuleEnabled(string archetypeId, string moduleId);
    string? GetClassification(string archetypeId, string moduleId);
    AgencyArchetypeEntry? GetArchetype(string archetypeId);
    CapabilityServiceEntry? GetService(string moduleId);
}

public sealed class CapabilityRegistry : ICapabilityRegistry
{
    public const string ArchetypeHeaderName = "X-Agency-Archetype";
    public const string ArchetypeClaimType = "agency_archetype";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<CapabilityRegistry> _logger;

    public CapabilityRegistry(
        IOptions<CapabilityProfileOptions> options,
        IHostEnvironment environment,
        ILogger<CapabilityRegistry> logger)
    {
        _logger = logger;
        Options = options.Value;
        CapabilityMap = LoadCapabilityMap(environment);
        Archetypes = LoadArchetypes(environment);
    }

    public CapabilityProfileOptions Options { get; }

    public CapabilityMapDocument CapabilityMap { get; }

    public AgencyArchetypesDocument Archetypes { get; }

    public bool IsModuleEnabled(string archetypeId, string moduleId)
    {
        var classification = GetClassification(archetypeId, moduleId);
        return classification is not null
               && !string.Equals(classification, "disabled", StringComparison.OrdinalIgnoreCase);
    }

    public string? GetClassification(string archetypeId, string moduleId)
    {
        if (string.IsNullOrWhiteSpace(archetypeId) || string.IsNullOrWhiteSpace(moduleId))
            return null;

        var archetype = GetArchetype(archetypeId);
        if (archetype?.ModuleEntitlements.TryGetValue(moduleId, out var fromArchetype) == true)
            return fromArchetype;

        var service = GetService(moduleId);
        if (service?.ArchetypeClassification.TryGetValue(archetypeId, out var fromMap) == true)
            return fromMap;

        return null;
    }

    public AgencyArchetypeEntry? GetArchetype(string archetypeId) =>
        Archetypes.Archetypes.FirstOrDefault(a =>
            string.Equals(a.Id, archetypeId, StringComparison.OrdinalIgnoreCase));

    public CapabilityServiceEntry? GetService(string moduleId) =>
        CapabilityMap.Services.FirstOrDefault(s =>
            string.Equals(s.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));

    public static string? ResolveArchetype(HttpContext context, string? configuredDefault)
    {
        if (context.Request.Headers.TryGetValue(ArchetypeHeaderName, out var header)
            && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString().Trim();
        }

        var claim = context.User?.FindFirst(ArchetypeClaimType)?.Value;
        if (!string.IsNullOrWhiteSpace(claim))
            return claim.Trim();

        return string.IsNullOrWhiteSpace(configuredDefault) ? null : configuredDefault.Trim();
    }

    private CapabilityMapDocument LoadCapabilityMap(IHostEnvironment environment)
    {
        var path = ResolvePath(
            environment,
            Options.CapabilityMapPath,
            "capability-map.json",
            Path.Combine("docs", "prd-product", "capability-map.json"),
            "ProductCapability");

        if (path is null || !File.Exists(path))
            return LoadEmbeddedCapabilityMap();

        _logger.LogInformation("Loading PRD-001 capability map from {Path}", path);
        var doc = Deserialize<CapabilityMapDocument>(path);
        ValidateCard(doc.Card, "PRD-001", "capability map");
        return doc;
    }

    private AgencyArchetypesDocument LoadArchetypes(IHostEnvironment environment)
    {
        var path = ResolvePath(
            environment,
            Options.ArchetypesPath,
            "agency-archetypes.json",
            Path.Combine("docs", "prd-product", "agency-archetypes.json"),
            "ProductCapability");

        if (path is null || !File.Exists(path))
            return LoadEmbeddedArchetypes();

        _logger.LogInformation("Loading PRD-001 agency archetypes from {Path}", path);
        var doc = Deserialize<AgencyArchetypesDocument>(path);
        ValidateCard(doc.Card, "PRD-001", "agency archetypes");
        return doc;
    }

    private static CapabilityMapDocument LoadEmbeddedCapabilityMap()
    {
        const string json = """
            {
              "card": "PRD-001",
              "version": 1,
              "asOfDate": "2026-07-31",
              "capabilityDomains": [],
              "services": [
                { "moduleId": "hr-core", "capabilityDomain": "employee-lifecycle", "ownerRole": "hr-core-product", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "mandatory", "branch-office": "mandatory", "specialized-body": "mandatory" } },
                { "moduleId": "hr-contracts", "capabilityDomain": "employee-lifecycle", "ownerRole": "hr-contracts-product", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "mandatory", "branch-office": "configurable", "specialized-body": "mandatory" } },
                { "moduleId": "hr-health", "capabilityDomain": "health-wellness", "ownerRole": "hr-health-product", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "configurable", "branch-office": "optional", "specialized-body": "mandatory" } },
                { "moduleId": "hr-movements", "capabilityDomain": "workforce-movements", "ownerRole": "hr-movements-product", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "mandatory", "branch-office": "configurable", "specialized-body": "mandatory" } },
                { "moduleId": "hr-training", "capabilityDomain": "learning-talent", "ownerRole": "hr-training-product", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "configurable", "autonomous-institution": "optional", "branch-office": "optional", "specialized-body": "mandatory" } },
                { "moduleId": "payroll", "capabilityDomain": "compensation", "ownerRole": "payroll-product", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "mandatory", "branch-office": "configurable", "specialized-body": "mandatory" } },
                { "moduleId": "idcard", "capabilityDomain": "identity-credentials", "ownerRole": "idcard-product", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "configurable", "branch-office": "mandatory", "specialized-body": "configurable" } },
                { "moduleId": "records-archive", "capabilityDomain": "records-archival", "ownerRole": "records-product", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "mandatory", "branch-office": "configurable", "specialized-body": "mandatory" } },
                { "moduleId": "admin-logistics", "capabilityDomain": "admin-operations", "ownerRole": "admin-logistics-product", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "configurable", "governorate": "optional", "autonomous-institution": "optional", "branch-office": "optional", "specialized-body": "disabled" } },
                { "moduleId": "it-infra", "capabilityDomain": "platform-ops", "ownerRole": "platform-sre", "uatRequired": false, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "mandatory", "branch-office": "mandatory", "specialized-body": "mandatory" } },
                { "moduleId": "dev-analytics", "capabilityDomain": "analytics-insights", "ownerRole": "data-analytics", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "configurable", "governorate": "optional", "autonomous-institution": "optional", "branch-office": "disabled", "specialized-body": "mandatory" } },
                { "moduleId": "compliance", "capabilityDomain": "compliance-governance", "ownerRole": "compliance-product", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "mandatory", "branch-office": "mandatory", "specialized-body": "mandatory" } },
                { "moduleId": "auth-directory", "capabilityDomain": "security-identity", "ownerRole": "iam-platform", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "mandatory", "branch-office": "mandatory", "specialized-body": "mandatory" } },
                { "moduleId": "file-storage", "capabilityDomain": "document-management", "ownerRole": "file-platform", "uatRequired": true, "sloRef": "embedded", "archetypeClassification": { "central-ministry": "mandatory", "governorate": "mandatory", "autonomous-institution": "mandatory", "branch-office": "mandatory", "specialized-body": "mandatory" } }
              ]
            }
            """;
        return JsonSerializer.Deserialize<CapabilityMapDocument>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize embedded capability map.");
    }

    private static AgencyArchetypesDocument LoadEmbeddedArchetypes()
    {
        const string json = """
            {
              "card": "PRD-001",
              "version": 1,
              "asOfDate": "2026-07-31",
              "allowedEntitlements": ["mandatory", "optional", "configurable", "disabled"],
              "requiredModuleIds": ["hr-core", "hr-contracts", "hr-health", "hr-movements", "hr-training", "payroll", "idcard", "records-archive", "admin-logistics", "it-infra", "dev-analytics", "compliance", "auth-directory", "file-storage"],
              "archetypes": [
                { "id": "central-ministry", "nameAr": "وزارة مركزية", "nameEn": "Central Ministry", "description": "embedded", "scaleTier": "xl", "moduleEntitlements": { "hr-core": "mandatory", "hr-contracts": "mandatory", "hr-health": "mandatory", "hr-movements": "mandatory", "hr-training": "mandatory", "payroll": "mandatory", "idcard": "mandatory", "records-archive": "mandatory", "admin-logistics": "configurable", "it-infra": "mandatory", "dev-analytics": "configurable", "compliance": "mandatory", "auth-directory": "mandatory", "file-storage": "mandatory" } },
                { "id": "governorate", "nameAr": "محافظة", "nameEn": "Governorate", "description": "embedded", "scaleTier": "large", "moduleEntitlements": { "hr-core": "mandatory", "hr-contracts": "mandatory", "hr-health": "mandatory", "hr-movements": "mandatory", "hr-training": "configurable", "payroll": "mandatory", "idcard": "mandatory", "records-archive": "mandatory", "admin-logistics": "optional", "it-infra": "mandatory", "dev-analytics": "optional", "compliance": "mandatory", "auth-directory": "mandatory", "file-storage": "mandatory" } },
                { "id": "autonomous-institution", "nameAr": "مؤسسة مستقلة", "nameEn": "Autonomous Institution", "description": "embedded", "scaleTier": "medium", "moduleEntitlements": { "hr-core": "mandatory", "hr-contracts": "mandatory", "hr-health": "configurable", "hr-movements": "mandatory", "hr-training": "optional", "payroll": "mandatory", "idcard": "configurable", "records-archive": "mandatory", "admin-logistics": "optional", "it-infra": "mandatory", "dev-analytics": "optional", "compliance": "mandatory", "auth-directory": "mandatory", "file-storage": "mandatory" } },
                { "id": "branch-office", "nameAr": "فرع ميداني", "nameEn": "Branch Office", "description": "embedded", "scaleTier": "small", "moduleEntitlements": { "hr-core": "mandatory", "hr-contracts": "configurable", "hr-health": "optional", "hr-movements": "configurable", "hr-training": "optional", "payroll": "configurable", "idcard": "mandatory", "records-archive": "configurable", "admin-logistics": "optional", "it-infra": "mandatory", "dev-analytics": "disabled", "compliance": "mandatory", "auth-directory": "mandatory", "file-storage": "mandatory" } },
                { "id": "specialized-body", "nameAr": "هيئة متخصصة", "nameEn": "Specialized Body", "description": "embedded", "scaleTier": "medium", "moduleEntitlements": { "hr-core": "mandatory", "hr-contracts": "mandatory", "hr-health": "mandatory", "hr-movements": "mandatory", "hr-training": "mandatory", "payroll": "mandatory", "idcard": "configurable", "records-archive": "mandatory", "admin-logistics": "disabled", "it-infra": "mandatory", "dev-analytics": "mandatory", "compliance": "mandatory", "auth-directory": "mandatory", "file-storage": "mandatory" } }
              ]
            }
            """;
        return JsonSerializer.Deserialize<AgencyArchetypesDocument>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize embedded archetypes.");
    }

    private static T Deserialize<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidOperationException($"Failed to deserialize {Path.GetFileName(path)}");

    private static void ValidateCard(string card, string expected, string label)
    {
        if (!string.Equals(card, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label} card must be {expected}, got '{card}'.");
    }

    private static string? ResolvePath(
        IHostEnvironment environment,
        string? configured,
        string fileName,
        string repoRelative,
        string outputSubfolder)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Path.IsPathRooted(configured) && File.Exists(configured))
                return configured;
            var fromContent = Path.Combine(environment.ContentRootPath, configured);
            if (File.Exists(fromContent)) return fromContent;
            var fromBase = Path.Combine(AppContext.BaseDirectory, configured);
            if (File.Exists(fromBase)) return fromBase;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, fileName),
                     Path.Combine(AppContext.BaseDirectory, outputSubfolder, fileName),
                     Path.Combine(environment.ContentRootPath, fileName),
                     Path.Combine(environment.ContentRootPath, outputSubfolder, fileName)
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        foreach (var start in new[]
                 {
                     new DirectoryInfo(environment.ContentRootPath),
                     new DirectoryInfo(AppContext.BaseDirectory)
                 })
        {
            var dir = start;
            while (dir is not null)
            {
                var repoPath = Path.Combine(dir.FullName, repoRelative);
                if (File.Exists(repoPath)) return repoPath;
                dir = dir.Parent;
            }
        }

        return null;
    }
}
