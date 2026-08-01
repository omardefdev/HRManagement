using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ExtCivil;

public sealed class ExtCivilProvider : IExtCivilProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ExtCivilProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ExtCivilProvider(
        IOptions<ExtCivilOptions> options,
        IHostEnvironment environment,
        ILogger<ExtCivilProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ExtCivilOptions Options { get; }
    public ExtCivilMatrixDocument Matrix { get; }

    private ExtCivilMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "civil-registry-matrix.json",
            Path.Combine("docs", "ext-civil", "civil-registry-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "EXT-002 civil-registry-matrix.json not found. Set ExtCivil:MatrixPath or deploy next to the app.",
                path ?? "civil-registry-matrix.json");
        }

        _logger.LogInformation("Loading EXT-002 civil-registry matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ExtCivilMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize civil-registry-matrix.json");
        if (!string.Equals(doc.Card, "EXT-002", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Civil registry matrix card must be EXT-002, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ExtCivil", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ExtCivil", fileName)
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
