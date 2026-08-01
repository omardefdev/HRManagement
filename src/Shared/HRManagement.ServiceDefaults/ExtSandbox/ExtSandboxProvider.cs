using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ExtSandbox;

public sealed class ExtSandboxProvider : IExtSandboxProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ExtSandboxProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ExtSandboxProvider(
        IOptions<ExtSandboxOptions> options,
        IHostEnvironment environment,
        ILogger<ExtSandboxProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ExtSandboxOptions Options { get; }
    public ExtSandboxMatrixDocument Matrix { get; }

    private ExtSandboxMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "sandbox-cert-matrix.json",
            Path.Combine("docs", "ext-sandbox", "sandbox-cert-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "EXT-006 sandbox-cert-matrix.json not found. Set ExtSandbox:MatrixPath or deploy next to the app.",
                path ?? "sandbox-cert-matrix.json");
        }

        _logger.LogInformation("Loading EXT-006 sandbox-cert matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ExtSandboxMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize sandbox-cert-matrix.json");
        if (!string.Equals(doc.Card, "EXT-006", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Sandbox matrix card must be EXT-006, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ExtSandbox", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ExtSandbox", fileName)
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
