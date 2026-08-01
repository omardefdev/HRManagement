using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ExtApi;

public sealed class ExtApiProvider : IExtApiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ExtApiProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ExtApiProvider(
        IOptions<ExtApiOptions> options,
        IHostEnvironment environment,
        ILogger<ExtApiProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ExtApiOptions Options { get; }
    public ExtApiMatrixDocument Matrix { get; }

    private ExtApiMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "api-exchange-matrix.json",
            Path.Combine("docs", "ext-api", "api-exchange-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "EXT-005 api-exchange-matrix.json not found. Set ExtApi:MatrixPath or deploy next to the app.",
                path ?? "api-exchange-matrix.json");
        }

        _logger.LogInformation("Loading EXT-005 api-exchange matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ExtApiMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize api-exchange-matrix.json");
        if (!string.Equals(doc.Card, "EXT-005", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"API exchange matrix card must be EXT-005, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ExtApi", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ExtApi", fileName)
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
