using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payroll.Application.Frd;

public interface IFrdAnomalyRulesProvider
{
    FrdAnomalyRulesDocument Document { get; }
    FrdDetectionRule? FindRule(string ruleId);
    bool HoldOnProofOfLifeExpiry { get; }
    bool SingleOperatorReleaseBlocked { get; }
}

public sealed class FrdAnomalyRulesOptions
{
    public const string SectionName = "FrdAnomalyRules";
    public string? RulesPath { get; set; }
}

public sealed class FrdAnomalyRulesProvider : IFrdAnomalyRulesProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public FrdAnomalyRulesProvider(
        IOptions<FrdAnomalyRulesOptions> options,
        IHostEnvironment environment,
        ILogger<FrdAnomalyRulesProvider> logger)
    {
        Document = Load(options.Value, environment, logger);
    }

    public FrdAnomalyRulesDocument Document { get; }

    public FrdDetectionRule? FindRule(string ruleId) =>
        Document.DetectionRules.FirstOrDefault(r =>
            string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));

    public bool HoldOnProofOfLifeExpiry => Document.ProofOfLife?.HoldOnExpiry ?? true;

    public bool SingleOperatorReleaseBlocked => Document.PaymentHold?.SingleOperatorReleaseBlocked ?? true;

    private static FrdAnomalyRulesDocument Load(
        FrdAnomalyRulesOptions options,
        IHostEnvironment environment,
        ILogger logger)
    {
        var path = ResolvePath(options.RulesPath, environment.ContentRootPath);
        logger.LogInformation("Loading FRD-001 anomaly rules from {Path}", path);
        var doc = JsonSerializer.Deserialize<FrdAnomalyRulesDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize anomaly-rules.json");
        if (!string.Equals(doc.Card, "FRD-001", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Anomaly rules card must be FRD-001, got '{doc.Card}'.");
        return doc;
    }

    private static string ResolvePath(string? configured, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Path.IsPathRooted(configured) && File.Exists(configured))
                return configured;
            var fromContent = Path.Combine(contentRoot, configured);
            if (File.Exists(fromContent))
                return fromContent;
        }

        var dir = new DirectoryInfo(contentRoot);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "frd", "anomaly-rules.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("docs/frd/anomaly-rules.json not found. Set FrdAnomalyRules:RulesPath.");
    }
}
