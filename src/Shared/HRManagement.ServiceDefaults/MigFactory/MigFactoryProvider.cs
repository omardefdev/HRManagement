using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.MigFactory;

public sealed class MigFactoryProvider : IMigFactoryProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<MigFactoryProvider> _logger;
    private readonly IHostEnvironment _environment;

    public MigFactoryProvider(
        IOptions<MigFactoryOptions> options,
        IHostEnvironment environment,
        ILogger<MigFactoryProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public MigFactoryOptions Options { get; }
    public MigFactoryMatrixDocument Matrix { get; }

    private MigFactoryMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "pipeline-matrix.json",
            Path.Combine("docs", "mig-factory", "pipeline-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "MIG-003 pipeline-matrix.json not found. Set MigFactory:MatrixPath or deploy next to the app.",
                path ?? "pipeline-matrix.json");
        }

        _logger.LogInformation("Loading MIG-003 pipeline matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<MigFactoryMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize pipeline-matrix.json");
        if (!string.Equals(doc.Card, "MIG-003", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Pipeline matrix card must be MIG-003, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "MigFactory", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "MigFactory", fileName)
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
