using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

/// <summary>SRE-003: incident command, severity model, status page, postmortem.</summary>
public class Sre003IncidentTests
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
    public void Sre003_docs_scripts_and_json_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/SRE_003_INCIDENT_COMMAND.md",
                     "docs/sre/incident-severity-model.json",
                     "docs/sre/status-page-templates.md",
                     "docs/sre/postmortem-template.md",
                     "scripts/sre003-validate.ps1"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }

    [Fact]
    public void Severity_model_has_four_levels_and_sev1_sla()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "sre", "incident-severity-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("SRE-003", root.GetProperty("card").GetString());

        var severities = root.GetProperty("severities");
        Assert.True(severities.GetArrayLength() >= 4);

        var sev1 = severities.EnumerateArray()
            .First(s => s.GetProperty("level").GetString() == "Sev-1");
        Assert.True(sev1.GetProperty("sla").GetProperty("firstCommMinutes").GetInt32() <= 15);
        Assert.True(sev1.GetProperty("sla").GetProperty("incidentCommanderRequired").GetBoolean());
    }

    [Fact]
    public void Severity_model_defines_incident_roles()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "sre", "incident-severity-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var roles = doc.RootElement.GetProperty("roles").EnumerateArray()
            .Select(r => r.GetProperty("role").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Incident Commander", roles);
        Assert.Contains("Operations Lead", roles);
        Assert.Contains("Communications Lead", roles);
    }

    [Fact]
    public void Incident_command_doc_covers_blameless_postmortem()
    {
        var doc = Read("docs/SRE_003_INCIDENT_COMMAND.md");
        Assert.Contains("Incident Commander", doc);
        Assert.Contains("blameless", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("postmortem", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_page_templates_cover_full_lifecycle()
    {
        var doc = Read("docs/sre/status-page-templates.md");
        Assert.Contains("Investigating", doc);
        Assert.Contains("Identified", doc);
        Assert.Contains("Monitoring", doc);
        Assert.Contains("Resolved", doc);
    }

    [Fact]
    public void Postmortem_template_has_required_sections()
    {
        var doc = Read("docs/sre/postmortem-template.md");
        Assert.Contains("Root cause", doc);
        Assert.Contains("Action items", doc);
        Assert.Contains("Timeline", doc);
        Assert.Contains("Lessons learned", doc);
        Assert.Contains("Blameless", doc);
    }

    [Fact]
    public void Sre003_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        var script = Path.Combine(root, "scripts", "sre003-validate.ps1");
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
        Assert.True(proc.ExitCode == 0, $"sre003-validate failed: {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }
}
