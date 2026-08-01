using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

/// <summary>REL-005: progressive delivery, canary gates, rollback SLA.</summary>
public class Rel005ProgressiveDeliveryTests
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
    public void Rel005_docs_scripts_and_json_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/REL_005_PROGRESSIVE_DELIVERY.md",
                     "docs/rel/canary-gates.json",
                     "scripts/rel005-validate.ps1"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }

    [Fact]
    public void Canary_gates_have_stages_and_rollback_sla()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "rel", "canary-gates.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("REL-005", root.GetProperty("card").GetString());

        var stages = root.GetProperty("stages");
        Assert.True(stages.GetArrayLength() >= 4);

        Assert.True(root.GetProperty("rollbackSlaMinutes").GetInt32() <= 15);
    }

    [Fact]
    public void Canary_gates_include_health_and_business_gates()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "rel", "canary-gates.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var allGates = doc.RootElement.GetProperty("stages").EnumerateArray()
            .SelectMany(s =>
            {
                if (s.TryGetProperty("gates", out var g))
                    return g.EnumerateArray().Select(gate => gate.GetProperty("type").GetString());
                return Enumerable.Empty<string?>();
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("health", allGates);
        Assert.Contains("business", allGates);
    }

    [Fact]
    public void Canary_gates_define_abort_policy()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "rel", "canary-gates.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var abort = doc.RootElement.GetProperty("abortPolicy");
        Assert.Equal("automatic-rollback", abort.GetProperty("onGateFail").GetString());
        Assert.Equal("immediate-rollback", abort.GetProperty("onManualAbort").GetString());
    }

    [Fact]
    public void Progressive_delivery_doc_covers_canary_rollback_flags()
    {
        var doc = Read("docs/REL_005_PROGRESSIVE_DELIVERY.md");
        Assert.Contains("canary", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("15 min", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("feature flag", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rel005_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        var script = Path.Combine(root, "scripts", "rel005-validate.ps1");
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
        Assert.True(proc.ExitCode == 0, $"rel005-validate failed: {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }
}
