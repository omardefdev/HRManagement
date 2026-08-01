using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.SovData;

public sealed class SovDataProvider : ISovDataProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<SovDataProvider> _logger;
    private readonly IHostEnvironment _environment;

    public SovDataProvider(
        IOptions<SovDataOptions> options,
        IHostEnvironment environment,
        ILogger<SovDataProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public SovDataOptions Options { get; }
    public SovDataMatrixDocument Matrix { get; }

    private SovDataMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "data-sovereignty-matrix.json",
            Path.Combine("docs", "sov-data", "data-sovereignty-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "SOV-002 data-sovereignty-matrix.json not found. Set SovData:MatrixPath or deploy next to the app.",
                path ?? "data-sovereignty-matrix.json");
        }

        _logger.LogInformation("Loading SOV-002 data-sovereignty matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<SovDataMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize data-sovereignty-matrix.json");
        if (!string.Equals(doc.Card, "SOV-002", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Data-sovereignty matrix card must be SOV-002, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "SovData", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "SovData", fileName)
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
