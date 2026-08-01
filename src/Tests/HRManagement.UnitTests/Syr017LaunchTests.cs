using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

public class Syr017LaunchTests
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
            var script = ResolveRepoFile("scripts", "syr017-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Plan_card_is_syr017()
    {
        var path = ResolveRepoFile("docs", "syr", "phased-launch-plan.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("SYR-017", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Baseline_volume_1_25m_in_4_hours()
    {
        var path = ResolveRepoFile("docs", "syr", "phased-launch-plan.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var vol = doc.RootElement.GetProperty("payrollVolume");
        Assert.True(vol.GetProperty("baselinePayrollCount").GetInt64() >= 1_250_000);
        Assert.True(vol.GetProperty("baselineWindowHours").GetInt32() >= 4);
        Assert.Equal(14, vol.GetProperty("governorateCount").GetInt32());
    }

    [Fact]
    public void Four_waves_no_auto_expand()
    {
        var path = ResolveRepoFile("docs", "syr", "phased-launch-plan.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var waves = doc.RootElement.GetProperty("waves").EnumerateArray().ToList();
        Assert.Equal(4, waves.Count);
        Assert.All(waves, w => Assert.False(w.GetProperty("autoExpand").GetBoolean()));
        Assert.All(waves, w => Assert.True(w.GetProperty("monitoringDays").GetInt32() >= 30));
    }

    [Fact]
    public void Nat008_cross_reference_required()
    {
        var path = ResolveRepoFile("docs", "syr", "phased-launch-plan.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var nat = doc.RootElement.GetProperty("nat008CrossRef");
        Assert.True(nat.GetProperty("releaseDigestRequired").GetBoolean());
        Assert.True(nat.GetProperty("certificationExpiryRequired").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "SYR_017_LAUNCH_CERT.md")));
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "syr", "phased-launch-plan.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = ResolveRepoFile("scripts", "syr017-validate.ps1");
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
