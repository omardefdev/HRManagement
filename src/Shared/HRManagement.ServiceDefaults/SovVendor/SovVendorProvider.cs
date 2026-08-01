using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.SovVendor;

public sealed class SovVendorProvider : ISovVendorProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<SovVendorProvider> _logger;
    private readonly IHostEnvironment _environment;

    public SovVendorProvider(
        IOptions<SovVendorOptions> options,
        IHostEnvironment environment,
        ILogger<SovVendorProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public SovVendorOptions Options { get; }
    public SovVendorMatrixDocument Matrix { get; }

    private SovVendorMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "vendor-exit-matrix.json",
            Path.Combine("docs", "sov-vendor", "vendor-exit-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "SOV-007 vendor-exit-matrix.json not found. Set SovVendor:MatrixPath or deploy next to the app.",
                path ?? "vendor-exit-matrix.json");
        }

        _logger.LogInformation("Loading SOV-007 vendor-exit matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<SovVendorMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize vendor-exit-matrix.json");
        if (!string.Equals(doc.Card, "SOV-007", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Vendor-exit matrix card must be SOV-007, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "SovVendor", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "SovVendor", fileName)
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
