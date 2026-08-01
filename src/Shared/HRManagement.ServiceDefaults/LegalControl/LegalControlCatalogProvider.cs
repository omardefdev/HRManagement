using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.LegalControl;

public sealed class LegalControlCatalogProvider : ILegalControlCatalogProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<LegalControlCatalogProvider> _logger;

    public LegalControlCatalogProvider(
        IOptions<LegalControlOptions> options,
        IHostEnvironment environment,
        ILogger<LegalControlCatalogProvider> logger)
    {
        _logger = logger;
        Options = options.Value;
        Catalog = LoadCatalog(environment);
        WatchStamp = LoadWatchStamp(environment);
        ReleaseProfile = NormalizeProfile(Options.ReleaseProfile);
    }

    public LegalControlOptions Options { get; }
    public ControlCatalogDocument Catalog { get; }
    public LegislativeWatchStamp? WatchStamp { get; }
    public string ReleaseProfile { get; }

    public CatalogControl? GetControl(string requirementId) =>
        Catalog.Controls.FirstOrDefault(c =>
            string.Equals(c.RequirementId, requirementId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<CatalogControl> GetControlsByDomain(string domain) =>
        Catalog.Controls
            .Where(c => string.Equals(c.Domain, domain, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public CatalogException? GetException(string exceptionId) =>
        Catalog.Exceptions.FirstOrDefault(e =>
            string.Equals(e.ExceptionId, exceptionId, StringComparison.OrdinalIgnoreCase));

    public bool IsExceptionActive(string? exceptionId)
    {
        if (string.IsNullOrWhiteSpace(exceptionId)) return false;
        var ex = GetException(exceptionId);
        return ex is not null
               && string.Equals(ex.Status, "Active", StringComparison.OrdinalIgnoreCase);
    }

    private ControlCatalogDocument LoadCatalog(IHostEnvironment environment)
    {
        var path = ResolvePath(
            environment,
            Options.CatalogPath,
            "control-catalog.json",
            "LegalControl",
            Path.Combine("docs", "str-legal", "control-catalog.json"));

        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "STR-002 control-catalog.json not found. Set LegalControl:CatalogPath or deploy the catalog next to the app.",
                path ?? "control-catalog.json");
        }

        _logger.LogInformation("Loading STR-002 control catalog from {Path}", path);
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<ControlCatalogDocument>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize control-catalog.json");
        if (!string.Equals(doc.Card, "STR-002", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Control catalog card must be STR-002, got '{doc.Card}'.");
        return doc;
    }

    private LegislativeWatchStamp? LoadWatchStamp(IHostEnvironment environment)
    {
        var path = ResolvePath(
            environment,
            Options.LegislativeWatchStampPath,
            "legislative-watch-stamp.json",
            "LegalControl",
            Path.Combine("docs", "str-legal", "legislative-watch-stamp.json"));

        if (path is null || !File.Exists(path))
        {
            _logger.LogWarning("STR-002 legislative-watch-stamp.json not found; watch gate will fail when enforced.");
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LegislativeWatchStamp>(json, JsonOptions);
    }

    private static string? ResolvePath(
        IHostEnvironment environment,
        string? configured,
        string fileName,
        string linkFolder,
        string repoRelative)
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
                     Path.Combine(AppContext.BaseDirectory, linkFolder, fileName),
                     Path.Combine(environment.ContentRootPath, fileName),
                     Path.Combine(environment.ContentRootPath, linkFolder, fileName)
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

    private static string NormalizeProfile(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Interim" : value.Trim();
}
