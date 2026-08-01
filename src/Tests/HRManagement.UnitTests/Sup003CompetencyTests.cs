using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Sup003CompetencyTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "sup003-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Competency_matrix_has_tier0_skills_with_two_deep_coverage()
    {
        var path = RepoPaths.Resolve("docs", "sup", "competency-matrix.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("SUP-003", root.GetProperty("card").GetString());

        var skills = root.GetProperty("skills").EnumerateArray().ToList();
        Assert.True(skills.Count >= 5, "must have at least 5 Tier-0 skills");

        foreach (var skill in skills)
        {
            Assert.Equal(0, skill.GetProperty("tier").GetInt32());
            var primary = skill.GetProperty("primary").GetString();
            var secondary = skill.GetProperty("secondary").GetString();
            Assert.NotNull(primary);
            Assert.NotNull(secondary);
            Assert.NotEqual(primary, secondary);
        }
    }

    [Fact]
    public void Coverage_policy_requires_minimum_depth_of_two()
    {
        var path = RepoPaths.Resolve("docs", "sup", "competency-matrix.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var policy = doc.RootElement.GetProperty("coveragePolicy");

        Assert.True(policy.GetProperty("minimumDepth").GetInt32() >= 2);
    }

    [Fact]
    public void Matrix_covers_key_domains()
    {
        var path = RepoPaths.Resolve("docs", "sup", "competency-matrix.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var skills = doc.RootElement.GetProperty("skills").EnumerateArray().ToList();

        var domains = skills.Select(s => s.GetProperty("domain").GetString()).Distinct().ToList();
        Assert.Contains("Database", domains);
        Assert.Contains("Identity", domains);
        Assert.Contains("Security", domains);
        Assert.Contains("Application", domains);
    }

    [Fact]
    public void Doc_covers_key_topics()
    {
        var path = RepoPaths.Resolve("docs", "SUP_003_COMPETENCY.md");
        var content = System.IO.File.ReadAllText(path);

        Assert.Contains("SUP-003", content);
        Assert.Contains("two-deep", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tier-0", content);
        Assert.Contains("Onboarding", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Offboarding", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Knowledge Transfer", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SUP_003_COMPETENCY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "sup", "competency-matrix.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sup003-validate.ps1");
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
