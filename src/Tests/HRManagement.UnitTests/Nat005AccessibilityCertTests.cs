using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

public class Nat005AccessibilityCertTests
{
    private static string ResolveRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (global::System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    private static string RepoRoot
    {
        get
        {
            var script = ResolveRepoFile("scripts", "nat005-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Checklist_card_is_nat005()
    {
        var path = ResolveRepoFile("docs", "nat", "nat005-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("NAT-005", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Wcag_22_aa_with_zero_blockers()
    {
        var path = ResolveRepoFile("docs", "nat", "nat005-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("2.2", doc.RootElement.GetProperty("wcagVersion").GetString());
        Assert.Equal("AA", doc.RootElement.GetProperty("conformanceLevel").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("accessibilityBlockers").GetInt32());
    }

    [Fact]
    public void Rtl_first_with_critical_journeys()
    {
        var path = ResolveRepoFile("docs", "nat", "nat005-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("localization").GetProperty("rtlFirst").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("criticalJourneys").GetArrayLength() >= 3);
    }

    [Fact]
    public void Field_acceptance_targets_defined()
    {
        var path = ResolveRepoFile("docs", "nat", "nat005-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var field = doc.RootElement.GetProperty("fieldAcceptance");
        Assert.True(field.GetProperty("disabilityRepresentationRequired").GetBoolean());
        Assert.True(field.GetProperty("taskSuccessTargetPercent").GetInt32() >= 90);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "NAT_005_ACCESSIBILITY_CERT.md")));
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "nat", "nat005-checklist.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = ResolveRepoFile("scripts", "nat005-validate.ps1");
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
