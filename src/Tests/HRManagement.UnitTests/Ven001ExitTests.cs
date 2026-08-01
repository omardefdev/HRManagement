using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Ven001ExitTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "ven001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Checklist_card_is_ven001()
    {
        var path = RepoPaths.Resolve("docs", "ven", "exit-drill-checklist.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("VEN-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Exit_rto_defined()
    {
        var path = RepoPaths.Resolve("docs", "ven", "exit-drill-checklist.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var hours = doc.RootElement.GetProperty("exitRtoHours").GetInt32();

        Assert.True(hours > 0);
        Assert.Equal("Blocked", doc.RootElement.GetProperty("liveDrillStatus").GetString());
    }

    [Fact]
    public void Phases_cover_transfer_rebuild_revoke()
    {
        var path = RepoPaths.Resolve("docs", "ven", "exit-drill-checklist.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var allStepIds = doc.RootElement.GetProperty("phases").EnumerateArray()
            .SelectMany(p => p.GetProperty("steps").EnumerateArray())
            .Select(s => s.GetProperty("stepId").GetString())
            .ToList();

        Assert.Contains(allStepIds, id => id!.Contains("revoke"));
        Assert.Contains(allStepIds, id => id!.Contains("rebuild") || id!.Contains("restore"));
        Assert.Contains(allStepIds, id => id!.Contains("attest") || id!.Contains("deletion"));
    }

    [Fact]
    public void Export_import_requires_full_reconciliation()
    {
        var path = RepoPaths.Resolve("docs", "ven", "exit-drill-checklist.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var policy = doc.RootElement.GetProperty("exportImportPolicy");

        Assert.True(policy.GetProperty("fullDataExportRequired").GetBoolean());
        Assert.Equal(100, policy.GetProperty("reconciliationPercent").GetInt32());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "VEN_001_VENDOR_EXIT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ven", "exit-drill-checklist.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ven001-validate.ps1");
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
