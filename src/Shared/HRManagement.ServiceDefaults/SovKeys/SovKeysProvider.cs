using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.SovKeys;

public sealed class SovKeysProvider : ISovKeysProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<SovKeysProvider> _logger;
    private readonly IHostEnvironment _environment;

    public SovKeysProvider(
        IOptions<SovKeysOptions> options,
        IHostEnvironment environment,
        ILogger<SovKeysProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public SovKeysOptions Options { get; }
    public SovKeysMatrixDocument Matrix { get; }

    private SovKeysMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "key-sovereignty-matrix.json",
            Path.Combine("docs", "sov-keys", "key-sovereignty-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "SOV-003 key-sovereignty-matrix.json not found. Set SovKeys:MatrixPath or deploy next to the app.",
                path ?? "key-sovereignty-matrix.json");
        }

        _logger.LogInformation("Loading SOV-003 key-sovereignty matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<SovKeysMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize key-sovereignty-matrix.json");
        if (!string.Equals(doc.Card, "SOV-003", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Key-sovereignty matrix card must be SOV-003, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "SovKeys", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "SovKeys", fileName)
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
