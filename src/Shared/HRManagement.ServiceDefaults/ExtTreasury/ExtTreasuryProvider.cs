using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ExtTreasury;

public sealed class ExtTreasuryProvider : IExtTreasuryProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ExtTreasuryProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ExtTreasuryProvider(
        IOptions<ExtTreasuryOptions> options,
        IHostEnvironment environment,
        ILogger<ExtTreasuryProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public ExtTreasuryOptions Options { get; }
    public ExtTreasuryMatrixDocument Matrix { get; }

    private ExtTreasuryMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "treasury-payment-matrix.json",
            Path.Combine("docs", "ext-treasury", "treasury-payment-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "EXT-003 treasury-payment-matrix.json not found. Set ExtTreasury:MatrixPath or deploy next to the app.",
                path ?? "treasury-payment-matrix.json");
        }

        _logger.LogInformation("Loading EXT-003 treasury-payment matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<ExtTreasuryMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize treasury-payment-matrix.json");
        if (!string.Equals(doc.Card, "EXT-003", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Treasury matrix card must be EXT-003, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "ExtTreasury", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ExtTreasury", fileName)
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
