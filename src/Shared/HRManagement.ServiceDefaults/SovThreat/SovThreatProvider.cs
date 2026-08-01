using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.SovThreat;

public sealed class SovThreatProvider : ISovThreatProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<SovThreatProvider> _logger;
    private readonly IHostEnvironment _environment;

    public SovThreatProvider(
        IOptions<SovThreatOptions> options,
        IHostEnvironment environment,
        ILogger<SovThreatProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public SovThreatOptions Options { get; }
    public SovThreatMatrixDocument Matrix { get; }

    private SovThreatMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "threat-model-matrix.json",
            Path.Combine("docs", "sov-threat", "threat-model-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "SOV-005 threat-model-matrix.json not found. Set SovThreat:MatrixPath or deploy next to the app.",
                path ?? "threat-model-matrix.json");
        }

        _logger.LogInformation("Loading SOV-005 threat-model matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<SovThreatMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize threat-model-matrix.json");
        if (!string.Equals(doc.Card, "SOV-005", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Threat-model matrix card must be SOV-005, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "SovThreat", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "SovThreat", fileName)
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
