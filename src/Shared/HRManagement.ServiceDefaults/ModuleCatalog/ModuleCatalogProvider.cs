using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ModuleCatalog;

public sealed class ModuleCatalogProvider : IModuleCatalogProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IHostEnvironment _environment;
    private readonly ILogger<ModuleCatalogProvider> _logger;

    public ModuleCatalogProvider(
        IOptions<ModuleCatalogOptions> options,
        IHostEnvironment environment,
        ILogger<ModuleCatalogProvider> logger)
    {
        _environment = environment;
        _logger = logger;
        Options = options.Value;
        Catalog = LoadCatalog();
    }

    public ModuleCatalogOptions Options { get; }
    public ModuleCatalogDocument Catalog { get; }

    public IReadOnlyList<string> PilotApprovedSlugs => Catalog.PilotApprovedSlugs;

    public ModuleCatalogEntry? GetModule(string moduleId) =>
        Catalog.Modules.FirstOrDefault(m =>
            string.Equals(m.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ModuleCatalogEntry> GetAgencyVisibleModules()
    {
        var tiers = Catalog.AgencyVisibleTiers.Count > 0
            ? Catalog.AgencyVisibleTiers
            : ["core", "extended"];
        return Catalog.Modules
            .Where(m => tiers.Contains(m.ServiceTier, StringComparer.OrdinalIgnoreCase)
                        && string.Equals(m.ReadinessStatus, "Ready", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public bool HasActiveException(string moduleId) =>
        Catalog.Exceptions.Any(e =>
            string.Equals(e.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase));

    public string? ResolveRepoRoot()
    {
        foreach (var start in new[]
                 {
                     new DirectoryInfo(_environment.ContentRootPath),
                     new DirectoryInfo(AppContext.BaseDirectory)
                 })
        {
            var dir = start;
            while (dir is not null)
            {
                var marker = Path.Combine(dir.FullName, "docs", "str-modules", "module-catalog.json");
                if (File.Exists(marker))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        return null;
    }

    private ModuleCatalogDocument LoadCatalog()
    {
        var path = ResolveCatalogPath();
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "STR-003 module-catalog.json not found. Set ModuleCatalog:CatalogPath or deploy the catalog next to the app.",
                path ?? "module-catalog.json");
        }

        _logger.LogInformation("Loading STR-003 module catalog from {Path}", path);
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<ModuleCatalogDocument>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize module-catalog.json");
        if (!string.Equals(doc.Card, "STR-003", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Module catalog card must be STR-003, got '{doc.Card}'.");
        return doc;
    }

    private string? ResolveCatalogPath()
    {
        if (!string.IsNullOrWhiteSpace(Options.CatalogPath))
        {
            var configured = Options.CatalogPath;
            if (Path.IsPathRooted(configured) && File.Exists(configured))
                return configured;
            var fromContent = Path.Combine(_environment.ContentRootPath, configured);
            if (File.Exists(fromContent)) return fromContent;
            var fromBase = Path.Combine(AppContext.BaseDirectory, configured);
            if (File.Exists(fromBase)) return fromBase;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "module-catalog.json"),
                     Path.Combine(AppContext.BaseDirectory, "ModuleCatalog", "module-catalog.json"),
                     Path.Combine(_environment.ContentRootPath, "module-catalog.json"),
                     Path.Combine(_environment.ContentRootPath, "ModuleCatalog", "module-catalog.json")
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        var repo = ResolveRepoRoot();
        if (repo is not null)
        {
            var repoPath = Path.Combine(repo, "docs", "str-modules", "module-catalog.json");
            if (File.Exists(repoPath)) return repoPath;
        }

        return null;
    }
}
