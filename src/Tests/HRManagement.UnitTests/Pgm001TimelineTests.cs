using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>PGM-001: tests for program timeline and critical path artifacts.</summary>
public class Pgm001TimelineTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "pgm001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Critical_path_json_exists_and_is_valid()
    {
        var path = RepoPaths.Resolve("docs", "pgm", "critical-path.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("PGM-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Zero_xl_cards_open_without_split()
    {
        var path = RepoPaths.Resolve("docs", "pgm", "critical-path.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal(0, doc.RootElement.GetProperty("xlDecomposition").GetProperty("openXlWithoutSplit").GetInt32());
    }

    [Fact]
    public void Critical_path_has_sovereign_and_engineering_segments()
    {
        var path = RepoPaths.Resolve("docs", "pgm", "critical-path.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var types = doc.RootElement.GetProperty("criticalPath").EnumerateArray()
            .Select(s => s.GetProperty("constraintType").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("sovereign-external", types);
        Assert.Contains("engineering", types);
    }

    [Fact]
    public void Baseline_links_to_fin001_and_cap003()
    {
        var path = RepoPaths.Resolve("docs", "pgm", "critical-path.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var baseline = doc.RootElement.GetProperty("baseline");
        Assert.True(baseline.GetProperty("linkedFin001").GetBoolean());
        Assert.True(baseline.GetProperty("linkedCap003").GetBoolean());
    }

    [Fact]
    public void Wave_one_slice_names_explicit_deferrals()
    {
        var path = RepoPaths.Resolve("docs", "pgm", "critical-path.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var deferred = doc.RootElement.GetProperty("waveOneMinimumSlice").GetProperty("explicitlyDeferred");
        Assert.True(deferred.GetArrayLength() >= 1);
    }

    [Fact]
    public void Doc_exists_and_addresses_program_timeline()
    {
        var path = RepoPaths.Resolve("docs", "PGM_001_TIMELINE.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("PGM-001", content);
        Assert.Contains("critical path", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("XL", content);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PGM_001_TIMELINE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "pgm", "critical-path.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "pgm001-validate.ps1");
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
