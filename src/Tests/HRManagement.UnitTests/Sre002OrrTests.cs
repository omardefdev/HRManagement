using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

/// <summary>SRE-002: ORR checklist, Tier-0 runbooks, validation script.</summary>
public class Sre002OrrTests
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
    public void Sre002_docs_scripts_and_json_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/SRE_002_ORR.md",
                     "docs/sre/orr-checklist-template.json",
                     "docs/runbooks/db-connection-failure.md",
                     "docs/runbooks/queue-backup.md",
                     "docs/runbooks/oom-kill.md",
                     "docs/runbooks/cert-expiry.md",
                     "scripts/sre002-validate.ps1"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }

    [Fact]
    public void Orr_checklist_has_required_sections_and_signoff()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "sre", "orr-checklist-template.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("SRE-002", root.GetProperty("card").GetString());

        var sectionIds = root.GetProperty("sections").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var required in new[] { "ownership", "slo-observability", "dependencies", "capacity", "deploy-rollback", "failure-runbooks" })
            Assert.Contains(required, sectionIds);

        Assert.True(root.TryGetProperty("signOff", out var signOff));
        Assert.True(signOff.TryGetProperty("serviceOwner", out _));
        Assert.True(signOff.TryGetProperty("sreLead", out _));
    }

    [Fact]
    public void Runbooks_cover_all_tier0_failure_modes()
    {
        var root = ResolveRepoRoot();
        var runbooks = new[] { "db-connection-failure", "queue-backup", "oom-kill", "cert-expiry" };

        foreach (var rb in runbooks)
        {
            var content = Read($"docs/runbooks/{rb}.md");
            Assert.Contains("SRE-002", content);
            Assert.Contains("Severity", content);
            Assert.Contains("Escalation", content);
            Assert.Contains("Mitigation", content);
        }
    }

    [Fact]
    public void Orr_doc_references_tier0_and_runbooks()
    {
        var doc = Read("docs/SRE_002_ORR.md");
        Assert.Contains("Tier-0", doc);
        Assert.Contains("runbook", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORR", doc);
        Assert.Contains("sign-off", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sre002_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        var script = Path.Combine(root, "scripts", "sre002-validate.ps1");
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
        Assert.True(proc.ExitCode == 0, $"sre002-validate failed: {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }
}
