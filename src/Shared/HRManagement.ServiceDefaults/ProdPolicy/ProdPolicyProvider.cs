using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ProdPolicy;

public sealed class ProdPolicyProvider : IProdPolicyProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ProdPolicyProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ProdPolicyProvider(
        IOptions<ProdPolicyOptions> options,
        IHostEnvironment environment,
        ILogger<ProdPolicyProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ProdPolicyOptions Options { get; }
    public ProdPolicyMatrixDocument Matrix { get; }

    private ProdPolicyMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "policy-platform-matrix.json",
            Path.Combine("docs", "prod-policy", "policy-platform-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "PROD-001 policy-platform-matrix.json not found. Set ProdPolicy:MatrixPath or deploy next to the app.",
                path ?? "policy-platform-matrix.json");
        }

        _logger.LogInformation("Loading PROD-001 policy-platform matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ProdPolicyMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize policy-platform-matrix.json");
        if (!string.Equals(doc.Card, "PROD-001", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Policy matrix card must be PROD-001, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ProdPolicy", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ProdPolicy", fileName)
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
