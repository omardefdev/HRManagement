using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ProdAgency;

public sealed class ProdAgencyProvider : IProdAgencyProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ProdAgencyProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ProdAgencyProvider(
        IOptions<ProdAgencyOptions> options,
        IHostEnvironment environment,
        ILogger<ProdAgencyProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ProdAgencyOptions Options { get; }
    public ProdAgencyMatrixDocument Matrix { get; }

    private ProdAgencyMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "agency-selfadmin-matrix.json",
            Path.Combine("docs", "prod-agency", "agency-selfadmin-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "PROD-002 agency-selfadmin-matrix.json not found. Set ProdAgency:MatrixPath or deploy next to the app.",
                path ?? "agency-selfadmin-matrix.json");
        }

        _logger.LogInformation("Loading PROD-002 agency-selfadmin matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ProdAgencyMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize agency-selfadmin-matrix.json");
        if (!string.Equals(doc.Card, "PROD-002", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Agency matrix card must be PROD-002, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ProdAgency", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ProdAgency", fileName)
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
