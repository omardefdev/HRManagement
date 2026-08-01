using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>PHY-001: tests for physical site autonomy artifacts.</summary>
public class Phy001PhysicalTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "phy001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Site_autonomy_json_exists_and_is_valid()
    {
        var path = RepoPaths.Resolve("docs", "phy", "site-autonomy.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("PHY-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Primary_and_dr_sites_have_dual_power_and_connectivity()
    {
        var path = RepoPaths.Resolve("docs", "phy", "site-autonomy.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var sites = doc.RootElement.GetProperty("sites").EnumerateArray().ToList();
        Assert.True(sites.Count >= 2);
        foreach (var site in sites)
        {
            Assert.True(site.GetProperty("power").GetProperty("dualFeed").GetBoolean());
            Assert.True(site.GetProperty("connectivity").GetProperty("dualProvider").GetBoolean());
            Assert.False(site.GetProperty("connectivity").GetProperty("singlePathAllowed").GetBoolean());
        }
    }

    [Fact]
    public void Branch_minimum_covers_14_governorates()
    {
        var path = RepoPaths.Resolve("docs", "phy", "site-autonomy.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal(14, doc.RootElement.GetProperty("branchMinimum").GetProperty("governorateCount").GetInt32());
    }

    [Fact]
    public void Verification_requires_measured_autonomy()
    {
        var path = RepoPaths.Resolve("docs", "phy", "site-autonomy.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("verification").GetProperty("measuredNotPaperOnly").GetBoolean());
    }

    [Fact]
    public void Doc_exists_and_covers_physical_requirements()
    {
        var path = RepoPaths.Resolve("docs", "PHY_001_PHYSICAL.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("PHY-001", content);
        Assert.Contains("generator", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cooling", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("branch", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PHY_001_PHYSICAL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "phy", "site-autonomy.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "phy001-validate.ps1");
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
