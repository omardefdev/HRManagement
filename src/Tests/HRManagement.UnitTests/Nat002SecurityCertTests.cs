using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

public class Nat002SecurityCertTests
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
            var script = ResolveRepoFile("scripts", "nat002-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Checklist_card_is_nat002()
    {
        var path = ResolveRepoFile("docs", "nat", "nat002-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("NAT-002", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Harness_done_live_blocked()
    {
        var path = ResolveRepoFile("docs", "nat", "nat002-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("Done", doc.RootElement.GetProperty("harnessStatus").GetString());
        Assert.Equal("Blocked", doc.RootElement.GetProperty("liveCertificationStatus").GetString());
    }

    [Fact]
    public void Sov_references_include_all_seven()
    {
        var path = ResolveRepoFile("docs", "nat", "nat002-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var refs = doc.RootElement.GetProperty("sovReferences").EnumerateArray()
            .Select(r => r.GetString()).ToList();
        Assert.Equal(7, refs.Count);
        Assert.Contains("SOV-006", refs);
    }

    [Fact]
    public void Residual_risk_requires_authority_signatory()
    {
        var path = ResolveRepoFile("docs", "nat", "nat002-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var domain = doc.RootElement.GetProperty("evidenceDomains").EnumerateArray()
            .First(d => d.GetProperty("id").GetString() == "residual-risk");
        Assert.Contains("authority", domain.GetProperty("signatoryRole").GetString()!);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "NAT_002_SECURITY_CERT.md")));
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "nat", "nat002-checklist.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = ResolveRepoFile("scripts", "nat002-validate.ps1");
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
