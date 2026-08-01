using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

public class Nat004MigrationCertTests
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
            var script = ResolveRepoFile("scripts", "nat004-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Checklist_card_is_nat004()
    {
        var path = ResolveRepoFile("docs", "nat", "nat004-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("NAT-004", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Mig_references_cover_full_chain()
    {
        var path = ResolveRepoFile("docs", "nat", "nat004-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var refs = doc.RootElement.GetProperty("migReferences").EnumerateArray()
            .Select(r => r.GetString()).ToList();
        Assert.Equal(7, refs.Count);
        Assert.Contains("MIG-005", refs);
        Assert.Contains("MIG-006", refs);
    }

    [Fact]
    public void Data_quality_requires_hundred_percent_accounted()
    {
        var path = ResolveRepoFile("docs", "nat", "nat004-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var dq = doc.RootElement.GetProperty("dataQuality");
        Assert.Equal(100, dq.GetProperty("accountedPercent").GetInt32());
        Assert.Equal(0, dq.GetProperty("criticalRecordGapCount").GetInt32());
    }

    [Fact]
    public void Rollback_tested_in_harness()
    {
        var path = ResolveRepoFile("docs", "nat", "nat004-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var cutover = doc.RootElement.GetProperty("cutover");
        Assert.True(cutover.GetProperty("rollbackTestedInHarness").GetBoolean());
        Assert.False(cutover.GetProperty("liveRollbackTested").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "NAT_004_MIGRATION_CERT.md")));
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "nat", "nat004-checklist.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = ResolveRepoFile("scripts", "nat004-validate.ps1");
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
