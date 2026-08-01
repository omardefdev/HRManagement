using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

/// <summary>SUP-002: service desk, SLA/OLA matrix, knowledge base structure.</summary>
public class Sup002ServiceDeskTests
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
    public void Sup002_docs_scripts_and_json_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/SUP_002_SERVICE_DESK.md",
                     "docs/sup/sla-ola-matrix.json",
                     "docs/sup/knowledge-base-structure.md",
                     "scripts/sup002-validate.ps1"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }

    [Fact]
    public void Sla_matrix_has_four_priorities_and_p1_response_sla()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "sup", "sla-ola-matrix.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("SUP-002", root.GetProperty("card").GetString());

        var priorities = root.GetProperty("priorities");
        Assert.True(priorities.GetArrayLength() >= 4);

        var p1 = priorities.EnumerateArray()
            .First(p => p.GetProperty("id").GetString() == "P1");
        Assert.True(p1.GetProperty("sla").GetProperty("responseMinutes").GetInt32() <= 15);
        Assert.True(p1.GetProperty("sla").GetProperty("resolutionHours").GetInt32() <= 4);
    }

    [Fact]
    public void Sla_matrix_defines_three_support_tiers()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "sup", "sla-ola-matrix.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var tiers = doc.RootElement.GetProperty("supportTiers").EnumerateArray()
            .Select(t => t.GetProperty("tier").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("L1", tiers);
        Assert.Contains("L2", tiers);
        Assert.Contains("L3", tiers);
    }

    [Fact]
    public void Sla_matrix_defines_metrics()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "sup", "sla-ola-matrix.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var metricIds = doc.RootElement.GetProperty("metrics").EnumerateArray()
            .Select(m => m.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("sla-achievement", metricIds);
        Assert.Contains("fcr", metricIds);
        Assert.Contains("csat", metricIds);
    }

    [Fact]
    public void Service_desk_doc_covers_sla_and_knowledge()
    {
        var doc = Read("docs/SUP_002_SERVICE_DESK.md");
        Assert.Contains("SLA", doc);
        Assert.Contains("knowledge", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("L1", doc);
        Assert.Contains("L2", doc);
        Assert.Contains("L3", doc);
    }

    [Fact]
    public void Knowledge_base_structure_defines_governance()
    {
        var doc = Read("docs/sup/knowledge-base-structure.md");
        Assert.Contains("top 20", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("template", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Quarterly", doc);
    }

    [Fact]
    public void Sup002_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        var script = Path.Combine(root, "scripts", "sup002-validate.ps1");
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
        Assert.True(proc.ExitCode == 0, $"sup002-validate failed: {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }
}
