using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ProdWorkbox;

public sealed class ProdWorkboxProvider : IProdWorkboxProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ProdWorkboxProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ProdWorkboxProvider(
        IOptions<ProdWorkboxOptions> options,
        IHostEnvironment environment,
        ILogger<ProdWorkboxProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ProdWorkboxOptions Options { get; }
    public ProdWorkboxMatrixDocument Matrix { get; }

    private ProdWorkboxMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "workbox-matrix.json",
            Path.Combine("docs", "prod-workbox", "workbox-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "PROD-005 workbox-matrix.json not found. Set ProdWorkbox:MatrixPath or deploy next to the app.",
                path ?? "workbox-matrix.json");
        }

        _logger.LogInformation("Loading PROD-005 workbox matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ProdWorkboxMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize workbox-matrix.json");
        if (!string.Equals(doc.Card, "PROD-005", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Workbox matrix card must be PROD-005, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ProdWorkbox", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ProdWorkbox", fileName)
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
