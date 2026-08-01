using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

/// <summary>SUP-004: vendor contracts, licenses, SLA, escalation paths.</summary>
public class Sup004ContractTests
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
    public void Sup004_registry_docs_and_scripts_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/sup-contracts/vendor-contract-registry.json",
                     "docs/SUP_004_ESCALATION_DRILL.md",
                     "scripts/sup004-license-audit.ps1",
                     "scripts/sup004-validate.ps1"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }

    [Fact]
    public void Vendor_registry_covers_tier0_components()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "sup-contracts", "vendor-contract-registry.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("SUP-004", root.GetProperty("card").GetString());

        var ids = root.GetProperty("vendors").EnumerateArray()
            .Select(v => v.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var must in new[] { "postgres", "rabbitmq", "keycloak", "hosting" })
            Assert.Contains(must, ids);

        foreach (var v in root.GetProperty("vendors").EnumerateArray())
        {
            Assert.True(v.TryGetProperty("sla", out var sla));
            Assert.True(sla.GetProperty("p1ResponseMinutes").GetInt32() > 0);
            Assert.True(v.TryGetProperty("escalation", out var esc));
            Assert.False(string.IsNullOrWhiteSpace(esc.GetProperty("l1").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(esc.GetProperty("l2").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(esc.GetProperty("l3").GetString()));
        }
    }

    [Fact]
    public void Escalation_drill_references_registry_and_sla()
    {
        var doc = Read("docs/SUP_004_ESCALATION_DRILL.md");
        Assert.Contains("vendor-contract-registry.json", doc);
        Assert.Contains("p1ResponseMinutes", doc);
        Assert.Contains("Quarterly", doc);
    }

    [Fact]
    public void Sup004_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        var script = Path.Combine(root, "scripts", "sup004-validate.ps1");
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
        Assert.True(proc.ExitCode == 0, $"sup004-validate failed: {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }
}
