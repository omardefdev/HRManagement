using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.MigOnboarding;

public sealed class MigOnboardingProvider : IMigOnboardingProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<MigOnboardingProvider> _logger;
    private readonly IHostEnvironment _environment;

    public MigOnboardingProvider(
        IOptions<MigOnboardingOptions> options,
        IHostEnvironment environment,
        ILogger<MigOnboardingProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadMatrix();
    }

    public MigOnboardingOptions Options { get; }
    public MigOnboardingMatrixDocument Matrix { get; }

    private MigOnboardingMatrixDocument LoadMatrix()
    {
        var path = ResolvePath(
            Options.MatrixPath,
            "onboarding-matrix.json",
            Path.Combine("docs", "mig-onboarding", "onboarding-matrix.json"));
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "MIG-007 onboarding-matrix.json not found. Set MigOnboarding:MatrixPath or deploy next to the app.",
                path ?? "onboarding-matrix.json");
        }

        _logger.LogInformation("Loading MIG-007 onboarding matrix from {Path}", path);
        var doc = JsonSerializer.Deserialize<MigOnboardingMatrixDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize onboarding-matrix.json");
        if (!string.Equals(doc.Card, "MIG-007", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Onboarding matrix card must be MIG-007, got '{doc.Card}'.");
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
                     Path.Combine(AppContext.BaseDirectory, "MigOnboarding", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "MigOnboarding", fileName)
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
