using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.SovZt;

public sealed class SovZtProvider : ISovZtProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<SovZtProvider> _logger;
    private readonly IHostEnvironment _environment;

    public SovZtProvider(
        IOptions<SovZtOptions> options,
        IHostEnvironment environment,
        ILogger<SovZtProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public SovZtOptions Options { get; }
    public SovZtMatrixDocument Matrix { get; }

    private SovZtMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "zero-trust-matrix.json",
            Path.Combine("docs", "sov-zt", "zero-trust-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "SOV-004 zero-trust-matrix.json not found. Set SovZt:MatrixPath or deploy next to the app.",
                path ?? "zero-trust-matrix.json");
        }

        _logger.LogInformation("Loading SOV-004 zero-trust matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<SovZtMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize zero-trust-matrix.json");
        if (!string.Equals(doc.Card, "SOV-004", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Zero-trust matrix card must be SOV-004, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "SovZt", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "SovZt", fileName)
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
