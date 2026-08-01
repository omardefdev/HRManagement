using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ArcReference;

public sealed class ArcReferenceProvider : IArcReferenceProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ArcReferenceProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ArcReferenceProvider(
        IOptions<ArcReferenceOptions> options,
        IHostEnvironment environment,
        ILogger<ArcReferenceProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ArcReferenceOptions Options { get; }
    public ArcReferenceMatrixDocument Matrix { get; }

    private ArcReferenceMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "reference-architecture-matrix.json",
            Path.Combine("docs", "arc-reference", "reference-architecture-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "ARC-002 reference-architecture-matrix.json not found. Set ArcReference:MatrixPath or deploy next to the app.",
                path ?? "reference-architecture-matrix.json");
        }

        _logger.LogInformation("Loading ARC-002 reference-architecture matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ArcReferenceMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize reference-architecture-matrix.json");
        if (!string.Equals(doc.Card, "ARC-002", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Reference-architecture matrix card must be ARC-002, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ArcReference", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ArcReference", fileName)
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
