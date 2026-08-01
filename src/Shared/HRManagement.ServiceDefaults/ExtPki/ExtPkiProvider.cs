using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ExtPki;

public sealed class ExtPkiProvider : IExtPkiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ExtPkiProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ExtPkiProvider(
        IOptions<ExtPkiOptions> options,
        IHostEnvironment environment,
        ILogger<ExtPkiProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ExtPkiOptions Options { get; }
    public ExtPkiMatrixDocument Matrix { get; }

    private ExtPkiMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "pki-signature-matrix.json",
            Path.Combine("docs", "ext-pki", "pki-signature-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "EXT-004 pki-signature-matrix.json not found. Set ExtPki:MatrixPath or deploy next to the app.",
                path ?? "pki-signature-matrix.json");
        }

        _logger.LogInformation("Loading EXT-004 pki-signature matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ExtPkiMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize pki-signature-matrix.json");
        if (!string.Equals(doc.Card, "EXT-004", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PKI matrix card must be EXT-004, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ExtPki", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ExtPki", fileName)
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
