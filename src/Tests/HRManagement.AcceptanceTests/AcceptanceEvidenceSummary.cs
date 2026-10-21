using System.Text.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>Rebuilds acceptance-evidence/summary.md and validates the eight-file contract.</summary>
public static class AcceptanceEvidenceSummary
{
    private static readonly string[] RequiredFiles =
    [
        "metadata.json", "steps.md", "before.json", "after.json",
        "audit.json", "db-checks.json", "timing.json", "result.json"
    ];

    public static string ResolveEvidenceRoot()
    {
        var env = Environment.GetEnvironmentVariable("ACCEPTANCE_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        return Path.Combine(AcceptanceHarness.RepoRoot, "acceptance-evidence");
    }

    public static SummaryReport Rebuild(string? evidenceRoot = null)
    {
        var root = evidenceRoot ?? ResolveEvidenceRoot();
        Directory.CreateDirectory(root);

        var report = new SummaryReport { EvidenceRoot = root };
        var acFolders = Directory.Exists(root)
            ? Directory.GetDirectories(root)
                .Where(d => Path.GetFileName(d).StartsWith("AC-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        foreach (var folder in acFolders)
        {
            var acCode = Path.GetFileName(folder);
            var entry = EvaluateFolder(folder, acCode);
            report.Entries.Add(entry);
        }

        WriteSummaryMarkdown(root, report);
        return report;
    }

    private static AcEvidenceEntry EvaluateFolder(string folder, string acCode)
    {
        var entry = new AcEvidenceEntry { AcCode = acCode };
        foreach (var file in RequiredFiles)
        {
            var path = Path.Combine(folder, file);
            if (File.Exists(path))
                entry.PresentFiles.Add(file);
            else
                entry.MissingFiles.Add(file);
        }

        if (Directory.Exists(Path.Combine(folder, "http")))
            entry.PresentFiles.Add("http/");

        var resultPath = Path.Combine(folder, "result.json");
        if (File.Exists(resultPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(resultPath));
                entry.Status = doc.RootElement.TryGetProperty("status", out var s)
                    ? s.GetString() ?? "Unknown"
                    : "Unknown";
                if (doc.RootElement.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                    entry.Reason = r.GetString();
            }
            catch
            {
                entry.Status = "Invalid";
            }
        }
        else
        {
            entry.Status = "NoResult";
        }

        entry.ContractComplete = entry.MissingFiles.Count == 0;
        return entry;
    }

    private static void WriteSummaryMarkdown(string root, SummaryReport report)
    {
        var path = Path.Combine(root, "summary.md");
        var lines = new List<string>
        {
            "# Acceptance evidence summary (AC-01..AC-37)",
            "",
            $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            "",
            "| AC | Status | Contract | Missing |",
            "|----|--------|----------|---------|"
        };

        foreach (var e in report.Entries)
        {
            var contract = e.ContractComplete ? "complete" : "incomplete";
            var missing = e.MissingFiles.Count == 0 ? "—" : string.Join(", ", e.MissingFiles);
            var reason = string.IsNullOrWhiteSpace(e.Reason) ? "" : $" ({e.Reason})";
            lines.Add($"| {e.AcCode} | {e.Status}{reason} | {contract} | {missing} |");
        }

        var expected = Enumerable.Range(1, 37).Select(n => $"AC-{n:D2}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = report.Entries.Select(e => e.AcCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notRun = expected.Except(found).OrderBy(x => x).ToList();
        if (notRun.Count > 0)
        {
            lines.Add("");
            lines.Add("## Not yet executed");
            foreach (var ac in notRun)
                lines.Add($"- {ac}");
        }

        lines.Add("");
        lines.Add($"**Totals:** {report.PassCount} Pass, {report.DeferredCount} Deferred/Blocked, {report.FailCount} Fail, {notRun.Count} not run.");
        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        report.SummaryPath = path;
    }

    public sealed class SummaryReport
    {
        public string EvidenceRoot { get; set; } = "";
        public string? SummaryPath { get; set; }
        public List<AcEvidenceEntry> Entries { get; } = [];
        public int PassCount => Entries.Count(e => string.Equals(e.Status, "Pass", StringComparison.OrdinalIgnoreCase));
        public int FailCount => Entries.Count(e => string.Equals(e.Status, "Fail", StringComparison.OrdinalIgnoreCase));
        public int DeferredCount => Entries.Count(e =>
            string.Equals(e.Status, "Deferred", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Status, "Blocked", StringComparison.OrdinalIgnoreCase));
    }

    public sealed class AcEvidenceEntry
    {
        public string AcCode { get; set; } = "";
        public string Status { get; set; } = "Unknown";
        public string? Reason { get; set; }
        public bool ContractComplete { get; set; }
        public List<string> PresentFiles { get; } = [];
        public List<string> MissingFiles { get; } = [];
    }
}
