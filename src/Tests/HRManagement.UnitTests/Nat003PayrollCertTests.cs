using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

public class Nat003PayrollCertTests
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
            var script = ResolveRepoFile("scripts", "nat003-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Checklist_card_is_nat003()
    {
        var path = ResolveRepoFile("docs", "nat", "nat003-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("NAT-003", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Requires_three_consecutive_cycles()
    {
        var path = ResolveRepoFile("docs", "nat", "nat003-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var cycles = doc.RootElement.GetProperty("payrollCycles");
        Assert.True(cycles.GetProperty("consecutiveRequired").GetInt32() >= 3);
        Assert.Equal(0, cycles.GetProperty("unexplainedVariancePercent").GetInt32());
    }

    [Fact]
    public void Golden_cases_include_maker_checker()
    {
        var path = ResolveRepoFile("docs", "nat", "nat003-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var ids = doc.RootElement.GetProperty("goldenCases").EnumerateArray()
            .Select(c => c.GetProperty("caseId").GetString()).ToList();
        Assert.True(ids.Count >= 5);
        Assert.Contains(ids, id => id!.Contains("maker-checker"));
    }

    [Fact]
    public void Reconciliation_requires_bank_and_treasury()
    {
        var path = ResolveRepoFile("docs", "nat", "nat003-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var recon = doc.RootElement.GetProperty("reconciliation");
        Assert.True(recon.GetProperty("bankReconciliationRequired").GetBoolean());
        Assert.True(recon.GetProperty("treasuryReconciliationRequired").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "NAT_003_PAYROLL_CERT.md")));
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "nat", "nat003-checklist.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = ResolveRepoFile("scripts", "nat003-validate.ps1");
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
