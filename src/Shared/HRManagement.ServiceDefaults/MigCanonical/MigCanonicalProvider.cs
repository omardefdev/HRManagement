using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.MigCanonical;

public sealed class MigCanonicalProvider : IMigCanonicalProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<MigCanonicalProvider> _logger;
    private readonly IHostEnvironment _environment;

    public MigCanonicalProvider(
        IOptions<MigCanonicalOptions> options,
        IHostEnvironment environment,
        ILogger<MigCanonicalProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public MigCanonicalOptions Options { get; }
    public MigCanonicalMatrixDocument Matrix { get; }

    private MigCanonicalMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "code-map-matrix.json",
            Path.Combine("docs", "mig-canonical", "code-map-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "MIG-002 code-map-matrix.json not found. Set MigCanonical:MatrixPath or deploy next to the app.",
                path ?? "code-map-matrix.json");
        }

        _logger.LogInformation("Loading MIG-002 code-map matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<MigCanonicalMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize code-map-matrix.json");
        if (!string.Equals(doc.Card, "MIG-002", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Code-map matrix card must be MIG-002, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "MigCanonical", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "MigCanonical", fileName)
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
