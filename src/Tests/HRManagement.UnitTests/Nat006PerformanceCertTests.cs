using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

public class Nat006PerformanceCertTests
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
            var script = ResolveRepoFile("scripts", "nat006-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Checklist_card_is_nat006()
    {
        var path = ResolveRepoFile("docs", "nat", "nat006-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("NAT-006", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Workload_envelope_matches_cap001()
    {
        var path = ResolveRepoFile("docs", "nat", "nat006-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var env = doc.RootElement.GetProperty("workloadEnvelope");
        Assert.True(env.GetProperty("soakHours").GetInt32() >= 72);
        Assert.True(env.GetProperty("stressLoadPercent").GetInt32() >= 150);
        Assert.True(env.GetProperty("spikeLoadPercent").GetInt32() >= 200);
        Assert.True(env.GetProperty("headroomPercent").GetInt32() >= 30);
    }

    [Fact]
    public void Cap001_reference_exists()
    {
        var path = ResolveRepoFile("docs", "nat", "nat006-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var capRef = doc.RootElement.GetProperty("cap001Ref").GetString()!;
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile(capRef.Split('/'))));
    }

    [Fact]
    public void Zero_loss_corruption_required()
    {
        var path = ResolveRepoFile("docs", "nat", "nat006-checklist.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("resilience")
            .GetProperty("zeroLossCorruptionRequired").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "NAT_006_PERFORMANCE_CERT.md")));
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "nat", "nat006-checklist.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = ResolveRepoFile("scripts", "nat006-validate.ps1");
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
