using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ProdEss;

public sealed class ProdEssProvider : IProdEssProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ProdEssProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ProdEssProvider(
        IOptions<ProdEssOptions> options,
        IHostEnvironment environment,
        ILogger<ProdEssProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ProdEssOptions Options { get; }
    public ProdEssMatrixDocument Matrix { get; }

    private ProdEssMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "employee-selfservice-matrix.json",
            Path.Combine("docs", "prod-ess", "employee-selfservice-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "PROD-003 employee-selfservice-matrix.json not found. Set ProdEss:MatrixPath or deploy next to the app.",
                path ?? "employee-selfservice-matrix.json");
        }

        _logger.LogInformation("Loading PROD-003 employee-selfservice matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ProdEssMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize employee-selfservice-matrix.json");
        if (!string.Equals(doc.Card, "PROD-003", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"ESS matrix card must be PROD-003, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ProdEss", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ProdEss", fileName)
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
