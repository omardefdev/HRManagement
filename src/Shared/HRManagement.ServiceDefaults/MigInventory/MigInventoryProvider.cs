using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.MigInventory;

public sealed class MigInventoryProvider : IMigInventoryProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<MigInventoryProvider> _logger;
    private readonly IHostEnvironment _environment;

    public MigInventoryProvider(
        IOptions<MigInventoryOptions> options,
        IHostEnvironment environment,
        ILogger<MigInventoryProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public MigInventoryOptions Options { get; }
    public MigInventoryMatrixDocument Matrix { get; }

    private MigInventoryMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "source-inventory.json",
            Path.Combine("docs", "mig-inventory", "source-inventory.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "MIG-001 source-inventory.json not found. Set MigInventory:MatrixPath or deploy next to the app.",
                path ?? "source-inventory.json");
        }

        _logger.LogInformation("Loading MIG-001 source inventory from {Path}", path);
        var doc = JsonSerializer.Deserialize<MigInventoryMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize source-inventory.json");
        if (!string.Equals(doc.Card, "MIG-001", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Inventory matrix card must be MIG-001, got '{doc.Card}'.");
        return doc;
    }

    private string? ResolvePath(string? configured, string fileName, string repoRelative)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Path.IsPathRooted(configured) && File.Exists(configured))
                return configured;
            var fromContent = Path.Combine(_environment.ContentRootPath, configured);
            if (File.Exists(fromContent)) return fromContent;
            var fromBase = Path.Combine(AppContext.BaseDirectory, configured);
            if (File.Exists(fromBase)) return fromBase;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, fileName),
                     Path.Combine(AppContext.BaseDirectory, "MigInventory", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "MigInventory", fileName)
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        foreach (var start in new[]
                 {
                     new DirectoryInfo(_environment.ContentRootPath),
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
