using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ProgramGovernance;

public sealed class ProgramGovernanceProvider : IProgramGovernanceProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ProgramGovernanceProvider> _logger;
    private readonly IHostEnvironment _environment;

    public ProgramGovernanceProvider(
        IOptions<ProgramGovernanceOptions> options,
        IHostEnvironment environment,
        ILogger<ProgramGovernanceProvider> logger)
    {
        _logger = logger;
        _environment = environment;
        Options = options.Value;
        Matrix = LoadRequired<GovernanceMatrixDocument>(
            Options.MatrixPath,
            "governance-matrix.json",
            Path.Combine("docs", "str-governance", "governance-matrix.json"),
            "STR-005");
        DecisionLog = LoadRequired<DecisionLogDocument>(
            Options.DecisionLogPath,
            "decision-log.json",
            Path.Combine("docs", "str-governance", "decision-log.json"),
            null);
        RiskRegister = LoadRequired<RiskRegisterDocument>(
            Options.RiskRegisterPath,
            "risk-register.json",
            Path.Combine("docs", "str-governance", "risk-register.json"),
            null);
        GoLiveEvidence = LoadOptional<GoLiveEvidenceDocument>(
            Options.GoLiveEvidencePath,
            "go-live-evidence.json",
            Path.Combine("testdata", "str005", "go-live", "multi-seat.json"));
    }

    public ProgramGovernanceOptions Options { get; }
    public GovernanceMatrixDocument Matrix { get; }
    public DecisionLogDocument DecisionLog { get; }
    public RiskRegisterDocument RiskRegister { get; }
    public GoLiveEvidenceDocument? GoLiveEvidence { get; }

    private T LoadRequired<T>(string? configured, string fileName, string repoRelative, string? expectedCard)
        where T : class
    {
        var path = ResolvePath(configured, fileName, repoRelative);
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                $"STR-005 {fileName} not found. Set ProgramGovernance path or deploy next to the app.",
                path ?? fileName);
        }

        _logger.LogInformation("Loading STR-005 {File} from {Path}", fileName, path);
        var doc = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException($"Failed to deserialize {fileName}");
        if (expectedCard is not null && doc is GovernanceMatrixDocument matrix
            && !string.Equals(matrix.Card, expectedCard, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Governance matrix card must be {expectedCard}, got '{matrix.Card}'.");
        }

        return doc;
    }

    private T? LoadOptional<T>(string? configured, string fileName, string repoRelative) where T : class
    {
        var path = ResolvePath(configured, fileName, repoRelative);
        if (path is null || !File.Exists(path))
        {
            _logger.LogWarning("STR-005 {File} not found; Go-Live evidence unavailable until configured", fileName);
            return null;
        }

        _logger.LogInformation("Loading STR-005 {File} from {Path}", fileName, path);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
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
                     Path.Combine(AppContext.BaseDirectory, "ProgramGovernance", fileName),
                     Path.Combine(_environment.ContentRootPath, fileName),
                     Path.Combine(_environment.ContentRootPath, "ProgramGovernance", fileName)
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
