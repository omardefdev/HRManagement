using System.Text.Json;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-35 (G-QA-07, D-QA-18, AC-QA-35f): <c>scripts/ci/codeql-sarif-gate.ps1</c> fails the CodeQL job on any result of
/// level error or security-severity &gt;= 7.0 that has no Open, unexpired entry in <c>docs/qa/codeql-baseline.json</c>,
/// and on a SARIF without any run. Fixtures: <c>testdata/qa/codeql-gate/</c>.
/// </summary>
public class CodeqlSarifGateTests
{
    private const string Fixtures = "testdata/qa/codeql-gate";
    private const string AsOf = "2026-11-01";

    private static (int Exit, string Output) Gate(string sarif, string baseline, string? outFile = null)
    {
        var args = $"-Sarif {Fixtures}/{sarif} -Baseline {baseline} -AsOfDate {AsOf}";
        if (outFile is not null)
            args += $" -OutFile \"{outFile}\"";
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(
            Path.Combine(RepoPaths.Root, "scripts", "ci", "codeql-sarif-gate.ps1"), args);
        return (exit, stdout + stderr);
    }

    [Fact]
    public void Passes_when_every_high_finding_is_in_an_open_baseline_entry()
    {
        var outFile = Path.Combine(Path.GetTempPath(), $"qa035-codeql-{Guid.NewGuid():N}.json");
        try
        {
            var (exit, output) = Gate("run.sarif", $"{Fixtures}/baseline-ok.json", outFile);
            Assert.True(exit == 0, output);
            Assert.Contains("3 result(s), 1 at level error or security-severity >= 7", output, StringComparison.Ordinal);
            using var report = JsonDocument.Parse(System.IO.File.ReadAllText(outFile));
            Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(1, report.RootElement.GetProperty("gated").GetInt32());
        }
        finally
        {
            if (System.IO.File.Exists(outFile))
                System.IO.File.Delete(outFile);
        }
    }

    [Fact]
    public void Fails_on_a_new_finding_with_security_severity_8_1()
    {
        // AC-QA-35f negative: security-severity 8.1, level warning, at a path the baseline does not know.
        var (exit, output) = Gate("new-high.sarif", $"{Fixtures}/baseline-ok.json");
        Assert.Equal(1, exit);
        Assert.Contains("new finding not in the baseline: cs/sql-injection at src/Fixture/NewQuery.cs:10 (level warning, security-severity 8.1)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Fails_on_an_error_level_finding_without_a_baseline_entry()
    {
        var (exit, output) = Gate("run.sarif", $"{Fixtures}/baseline-empty.json");
        Assert.Equal(1, exit);
        Assert.Contains("::error::new finding not in the baseline: cs/sql-injection at src/Fixture/Query.cs:10", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Fails_when_the_baseline_entry_is_past_its_review_date()
    {
        var (exit, output) = Gate("run.sarif", $"{Fixtures}/baseline-expired.json");
        Assert.Equal(1, exit);
        Assert.Contains("baseline entry 'CQ-FIXTURE-1' expired on 2026-10-01", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Fails_when_the_sarif_has_no_run()
    {
        var (exit, output) = Gate("empty.sarif", $"{Fixtures}/baseline-empty.json");
        Assert.Equal(1, exit);
        Assert.Contains("contains no run", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Medium_and_note_findings_do_not_fail_the_gate()
    {
        var (exit, output) = Gate("low-only.sarif", $"{Fixtures}/baseline-empty.json");
        Assert.True(exit == 0, output);
        Assert.Contains("0 at level error or security-severity >= 7", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_live_baseline_is_well_formed_and_registered()
    {
        var (exit, output) = Gate("low-only.sarif", "docs/qa/codeql-baseline.json");
        Assert.True(exit == 0, output);

        var registry = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "qa", "dated-obligations.json"));
        Assert.Contains("\"file\": \"docs/qa/codeql-baseline.json\"", registry, StringComparison.Ordinal);
    }
}
