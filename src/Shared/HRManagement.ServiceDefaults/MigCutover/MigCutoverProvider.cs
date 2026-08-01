using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.MigCutover;

public sealed class MigCutoverProvider : IMigCutoverProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<MigCutoverProvider> _logger;
    private readonly IHostEnvironment _environment;

    public MigCutoverProvider(
        IOptions<MigCutoverOptions> options,
        IHostEnvironment environment,
        ILogger<MigCutoverProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public MigCutoverOptions Options { get; }
    public MigCutoverMatrixDocument Matrix { get; }

    private MigCutoverMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "cutover-matrix.json",
            Path.Combine("docs", "mig-cutover", "cutover-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "MIG-006 cutover-matrix.json not found. Set MigCutover:MatrixPath or deploy next to the app.",
                path ?? "cutover-matrix.json");
        }

        _logger.LogInformation("Loading MIG-006 cutover matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<MigCutoverMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize cutover-matrix.json");
        if (!string.Equals(doc.Card, "MIG-006", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cutover matrix card must be MIG-006, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "MigCutover", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "MigCutover", fileName)
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
