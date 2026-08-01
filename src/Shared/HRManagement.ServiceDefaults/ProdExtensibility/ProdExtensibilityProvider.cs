using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ProdExtensibility;

public sealed class ProdExtensibilityProvider : IProdExtensibilityProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ProdExtensibilityProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ProdExtensibilityProvider(
        IOptions<ProdExtensibilityOptions> options,
        IHostEnvironment environment,
        ILogger<ProdExtensibilityProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ProdExtensibilityOptions Options { get; }
    public ProdExtensibilityMatrixDocument Matrix { get; }

    private ProdExtensibilityMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "extensibility-matrix.json",
            Path.Combine("docs", "prod-extensibility", "extensibility-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "PROD-007 extensibility-matrix.json not found. Set ProdExtensibility:MatrixPath or deploy next to the app.",
                path ?? "extensibility-matrix.json");
        }

        _logger.LogInformation("Loading PROD-007 extensibility matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ProdExtensibilityMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize extensibility-matrix.json");
        if (!string.Equals(doc.Card, "PROD-007", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Extensibility matrix card must be PROD-007, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ProdExtensibility", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ProdExtensibility", fileName)
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
