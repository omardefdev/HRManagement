using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

public class Nat007PilotCertTests
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
            var script = ResolveRepoFile("scripts", "nat007-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Checklist_card_is_nat007()
    {
        var path = ResolveRepoFile("docs", "nat", "nat007-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("NAT-007", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Rel002_rel003_required()
    {
        var path = ResolveRepoFile("docs", "nat", "nat007-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var req = doc.RootElement.GetProperty("pilotRequirements");
        Assert.True(req.GetProperty("rel002Complete").GetBoolean());
        Assert.True(req.GetProperty("rel003Complete").GetBoolean());
    }

    [Fact]
    public void Three_cycles_and_30_day_slo()
    {
        var path = ResolveRepoFile("docs", "nat", "nat007-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var req = doc.RootElement.GetProperty("pilotRequirements");
        Assert.True(req.GetProperty("consecutivePayrollCycles").GetInt32() >= 3);
        Assert.True(req.GetProperty("sloWindowDays").GetInt32() >= 30);
        Assert.Equal(0, req.GetProperty("severity1IncidentCount").GetInt32());
    }

    [Fact]
    public void Scorecard_domains_comprehensive()
    {
        var path = ResolveRepoFile("docs", "nat", "nat007-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("scorecardDomains").GetArrayLength() >= 10);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "NAT_007_PILOT_CERT.md")));
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "nat", "nat007-checklist.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = ResolveRepoFile("scripts", "nat007-validate.ps1");
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
