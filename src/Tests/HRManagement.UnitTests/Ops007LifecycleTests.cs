using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

/// <summary>OPS-007: component lifecycle, patch SLA, certificate expiry alerts.</summary>
public class Ops007LifecycleTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docker-compose.production.yml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Read(string relative) =>
        System.IO.File.ReadAllText(Path.Combine(ResolveRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Ops007_docs_scripts_and_json_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/OPS_007_COMPONENT_LIFECYCLE.md",
                     "docs/ops007/component-inventory.json",
                     "docs/ops007/patch-sla-policy.json",
                     "scripts/ops007-validate.ps1"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }

    [Fact]
    public void Component_inventory_covers_tier0_components()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "ops007", "component-inventory.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("OPS-007", root.GetProperty("card").GetString());

        var components = root.GetProperty("components");
        Assert.True(components.GetArrayLength() >= 5);

        var tier0 = components.EnumerateArray()
            .Where(c => c.GetProperty("tier").GetInt32() == 0)
            .ToList();
        Assert.True(tier0.Count >= 3);

        foreach (var c in tier0)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("owner").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("currentVersion").GetString()));
        }
    }

    [Fact]
    public void Patch_sla_policy_enforces_24h_exploited_critical()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "ops007", "patch-sla-policy.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("OPS-007", root.GetProperty("card").GetString());

        var slas = root.GetProperty("patchSLAs");
        Assert.True(slas.GetArrayLength() >= 4);

        var exploited = slas.EnumerateArray()
            .First(s => s.GetProperty("id").GetString() == "exploited-critical");
        Assert.True(exploited.GetProperty("slaHours").GetInt32() <= 24);
    }

    [Fact]
    public void Patch_sla_policy_has_cert_alert_windows()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "ops007", "patch-sla-policy.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var windows = doc.RootElement.GetProperty("certAlertWindows").EnumerateArray()
            .Select(w => w.GetProperty("daysBeforeExpiry").GetInt32())
            .ToList();

        Assert.Contains(60, windows);
        Assert.Contains(30, windows);
        Assert.Contains(15, windows);
        Assert.Contains(7, windows);
    }

    [Fact]
    public void Lifecycle_doc_covers_eol_and_patching()
    {
        var doc = Read("docs/OPS_007_COMPONENT_LIFECYCLE.md");
        Assert.Contains("EOL", doc);
        Assert.Contains("24 hour", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("60", doc);
        Assert.Contains("30", doc);
        Assert.Contains("15", doc);
        Assert.Contains("7", doc);
    }

    [Fact]
    public void Ops007_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        var script = Path.Combine(root, "scripts", "ops007-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{root}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"ops007-validate failed: {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }
}
