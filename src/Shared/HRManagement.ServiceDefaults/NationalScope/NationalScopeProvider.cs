using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.NationalScope;

public sealed class NationalScopeProvider : INationalScopeProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<NationalScopeProvider> _logger;

    public NationalScopeProvider(
        IOptions<NationalScopeOptions> options,
        IHostEnvironment environment,
        ILogger<NationalScopeProvider> logger)
    {
        _logger = logger;
        Options = options.Value;
        Matrix = LoadMatrix(environment);
        ActiveEnvelope = NormalizeEnvelope(Options.ActiveEnvelope);
        if (!Matrix.AllowedEnvelopes.Contains(ActiveEnvelope, StringComparer.OrdinalIgnoreCase)
            && Matrix.AllowedEnvelopes.Count > 0)
        {
            throw new InvalidOperationException(
                $"NationalScope:ActiveEnvelope '{ActiveEnvelope}' is not in allowedEnvelopes.");
        }
    }

    public NationalScopeOptions Options { get; }
    public EnvelopeMatrixDocument Matrix { get; }
    public string ActiveEnvelope { get; }

    public EnvelopeIndicator? GetIndicator(string indicatorId) =>
        Matrix.Indicators.FirstOrDefault(i =>
            string.Equals(i.Id, indicatorId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.Envelope, ActiveEnvelope, StringComparison.OrdinalIgnoreCase));

    public decimal? GetNumericCeiling(params string[] preferredIndicatorIds)
    {
        foreach (var id in preferredIndicatorIds)
        {
            var ind = GetIndicator(id);
            var n = ind?.AsDecimal();
            if (n.HasValue) return n;
        }

        // Fall back to national-competition for shared platform ceilings (e.g. tenants).
        foreach (var id in preferredIndicatorIds)
        {
            var ind = Matrix.Indicators.FirstOrDefault(i =>
                string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.Envelope, "national-competition", StringComparison.OrdinalIgnoreCase));
            var n = ind?.AsDecimal();
            if (n.HasValue) return n;
        }

        return null;
    }

    public decimal? TenantCeiling => GetNumericCeiling("national.tenants");

    public decimal? PersonsCeiling => GetNumericCeiling(
        "syria.active_history_persons",
        "national.active_history_persons");

    public decimal? PayrollWindowPersonsCeiling => GetNumericCeiling(
        "syria.payroll_ordinary_persons",
        "national.payroll_window_persons");

    public string ResolvedCurrencyCode
    {
        get
        {
            var syr = GetIndicator("syria.currency")?.AsString();
            if (!string.IsNullOrWhiteSpace(syr) && syr.StartsWith("SYP", StringComparison.OrdinalIgnoreCase))
                return "SYP";
            return string.IsNullOrWhiteSpace(Options.CurrencyCode) ? "SYP" : Options.CurrencyCode;
        }
    }

    private EnvelopeMatrixDocument LoadMatrix(IHostEnvironment environment)
    {
        var path = ResolveMatrixPath(environment);
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "STR-001 envelope-matrix.json not found. Set NationalScope:MatrixPath or deploy the matrix next to the app.",
                path ?? "envelope-matrix.json");
        }

        _logger.LogInformation("Loading STR-001 envelope matrix from {Path}", path);
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<EnvelopeMatrixDocument>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize envelope-matrix.json");
        if (!string.Equals(doc.Card, "STR-001", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Envelope matrix card must be STR-001, got '{doc.Card}'.");
        return doc;
    }

    private string? ResolveMatrixPath(IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(Options.MatrixPath))
        {
            var configured = Options.MatrixPath;
            if (Path.IsPathRooted(configured) && File.Exists(configured))
                return configured;
            var fromContent = Path.Combine(environment.ContentRootPath, configured);
            if (File.Exists(fromContent)) return fromContent;
            var fromBase = Path.Combine(AppContext.BaseDirectory, configured);
            if (File.Exists(fromBase)) return fromBase;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "envelope-matrix.json"),
                     Path.Combine(AppContext.BaseDirectory, "NationalScope", "envelope-matrix.json"),
                     Path.Combine(environment.ContentRootPath, "envelope-matrix.json"),
                     Path.Combine(environment.ContentRootPath, "NationalScope", "envelope-matrix.json")
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        // Walk up for repo-local docs/str-scope/envelope-matrix.json (dev/test).
        var dir = new DirectoryInfo(environment.ContentRootPath);
        while (dir is not null)
        {
            var repoPath = Path.Combine(dir.FullName, "docs", "str-scope", "envelope-matrix.json");
            if (File.Exists(repoPath)) return repoPath;
            dir = dir.Parent;
        }

        dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var repoPath = Path.Combine(dir.FullName, "docs", "str-scope", "envelope-matrix.json");
            if (File.Exists(repoPath)) return repoPath;
            dir = dir.Parent;
        }

        return null;
    }

    private static string NormalizeEnvelope(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "syria-interim" : value.Trim();
}
