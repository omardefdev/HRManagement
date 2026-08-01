using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

/// <summary>OPS-006: cyber recovery, clean-room rebuild, immutable artifacts, credential reset.</summary>
public class Ops006CyberRecoveryTests
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
    public void Ops006_docs_scripts_and_json_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/OPS_006_CYBER_RECOVERY.md",
                     "docs/OPS_006_CREDENTIAL_RESET.md",
                     "docs/OPS_006_IMMUTABLE_ARTIFACTS.md",
                     "docs/OPS_006_CLEANROOM_RUNBOOK.md",
                     "docs/ops006/immutable-artifact-pipeline.json",
                     "docs/ops006/assumed-breach-scenarios.json",
                     "scripts/ops006-validate.ps1"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }

    [Fact]
    public void Immutable_pipeline_forbids_in_place_rebuild_and_unsigned_images()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "ops006", "immutable-artifact-pipeline.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("OPS-006", root.GetProperty("card").GetString());

        var rules = root.GetProperty("cleanRoomRules");
        Assert.True(rules.GetProperty("rebuildInPlaceForbidden").GetBoolean());
        Assert.True(rules.GetProperty("credentialReuseForbidden").GetBoolean());
        Assert.True(rules.GetProperty("unsignedImageDeployForbidden").GetBoolean());

        var sources = root.GetProperty("sources").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("container-registry", sources);
        Assert.Contains("database-backup", sources);
    }

    [Fact]
    public void Assumed_breach_scenarios_cover_ransomware_and_poisoned_image()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "ops006", "assumed-breach-scenarios.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var ids = doc.RootElement.GetProperty("scenarios").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("ransomware-host-compromise", ids);
        Assert.Contains("poisoned-container-image", ids);
        Assert.True(doc.RootElement.GetProperty("scenarios").GetArrayLength() >= 4);
    }

    [Fact]
    public void Cyber_recovery_doc_defines_clean_room_and_cyber_rto()
    {
        var doc = Read("docs/OPS_006_CYBER_RECOVERY.md");
        Assert.Contains("clean-room", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cyber-RTO", doc);
        Assert.Contains("24 h", doc);
    }

    [Fact]
    public void Ops006_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        var script = Path.Combine(root, "scripts", "ops006-validate.ps1");
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
        Assert.True(proc.ExitCode == 0, $"ops006-validate failed: {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }
}
