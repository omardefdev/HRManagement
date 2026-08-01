using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Syr013BranchUxTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr013-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Checklist_card_is_syr013()
    {
        var path = RepoPaths.Resolve("docs", "syr", "accessibility-wcag-checklist.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("SYR-013", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Wcag_22_aa_required()
    {
        var path = RepoPaths.Resolve("docs", "syr", "accessibility-wcag-checklist.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("2.2", root.GetProperty("wcagVersion").GetString());
        Assert.Equal("AA", root.GetProperty("conformanceLevel").GetString());
        Assert.True(root.GetProperty("localization").GetProperty("rtlFirst").GetBoolean());
    }

    [Fact]
    public void All_14_governorates_listed()
    {
        var path = RepoPaths.Resolve("docs", "syr", "accessibility-wcag-checklist.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var governorates = doc.RootElement.GetProperty("governorates").EnumerateArray().ToList();

        Assert.Equal(14, governorates.Count);
        Assert.All(governorates, g => Assert.True(g.GetProperty("uatRequired").GetBoolean()));
    }

    [Fact]
    public void Critical_journeys_and_wcag_criteria_defined()
    {
        var path = RepoPaths.Resolve("docs", "syr", "accessibility-wcag-checklist.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var journeys = doc.RootElement.GetProperty("criticalJourneys").EnumerateArray().ToList();
        Assert.True(journeys.Count >= 3);

        var criteria = doc.RootElement.GetProperty("wcagCriteria").EnumerateArray().ToList();
        Assert.True(criteria.Count >= 5);
    }

    [Fact]
    public void Draft_policy_requires_encryption()
    {
        var path = RepoPaths.Resolve("docs", "syr", "accessibility-wcag-checklist.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var policy = doc.RootElement.GetProperty("draftPolicy");

        Assert.True(policy.GetProperty("encryptedSaveRequired").GetBoolean());
        Assert.True(policy.GetProperty("sensitiveDataInCacheForbidden").GetBoolean());
        Assert.True(policy.GetProperty("duplicateSubmitPrevention").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_013_BRANCH_UX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "accessibility-wcag-checklist.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr013-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
