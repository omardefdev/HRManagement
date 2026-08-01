using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Cap001EnvelopeTests
{

    private static JsonDocument LoadJson(params string[] parts)
    {
        var path = RepoPaths.Resolve(parts);
        return JsonDocument.Parse(System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void Matrix_defines_national_envelope_scenarios()
    {
        using var doc = LoadJson("perf", "scenarios", "cap001-national-envelope.json");
        var root = doc.RootElement;
        Assert.Equal("CAP-001", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("headroomTargetPct").GetDouble() >= 30);

        var ids = root.GetProperty("scenarios").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("soak-100", ids);
        Assert.Contains("stress-150", ids);
        Assert.Contains("spike-200", ids);
        Assert.Contains("failure-during-load", ids);
        Assert.Contains("regional-mix", ids);
        Assert.Contains("payroll-window", ids);

        var soak = root.GetProperty("scenarios").EnumerateArray()
            .First(s => s.GetProperty("id").GetString() == "soak-100");
        Assert.Equal("72h", soak.GetProperty("duration").GetString());
        Assert.Equal(100, soak.GetProperty("loadPct").GetInt32());

        var stress = root.GetProperty("scenarios").EnumerateArray()
            .First(s => s.GetProperty("id").GetString() == "stress-150");
        Assert.Equal("4h", stress.GetProperty("duration").GetString());
        Assert.Equal(150, stress.GetProperty("loadPct").GetInt32());

        var spike = root.GetProperty("scenarios").EnumerateArray()
            .First(s => s.GetProperty("id").GetString() == "spike-200");
        Assert.Equal("15m", spike.GetProperty("duration").GetString());
        Assert.Equal(200, spike.GetProperty("loadPct").GetInt32());
    }

    [Fact]
    public void Results_schema_requires_slo_latencies_errors_headroom()
    {
        using var doc = LoadJson("perf", "cap001-results-schema.json");
        var root = doc.RootElement;
        Assert.Equal("CAP-001 National Workload Envelope Results", root.GetProperty("title").GetString());

        var required = root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("slo", required);
        Assert.Contains("latencies", required);
        Assert.Contains("errorRates", required);
        Assert.Contains("headroom", required);

        var headroomTarget = root.GetProperty("properties")
            .GetProperty("headroom")
            .GetProperty("properties")
            .GetProperty("targetPct")
            .GetProperty("const");
        Assert.Equal(30, headroomTarget.GetDouble());
    }

    [Fact]
    public void Required_artifacts_and_scripts_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("perf", "k6", "cap001-soak-72h.js")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("perf", "k6", "cap001-stress-150.js")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("perf", "k6", "cap001-spike-200.js")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("perf", "k6", "cap001-failure-during-load.js")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("perf", "k6", "lib", "cap001.js")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "cap001-headroom-calculator.ps1")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "cap001-envelope-validate.ps1")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "CAP_001_NATIONAL_ENVELOPE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("perf", "docker-compose.cap001.yml")));
    }

    [Fact]
    public void K6_scripts_reference_cap001_helpers()
    {
        foreach (var script in new[] { "cap001-soak-72h.js", "cap001-stress-150.js", "cap001-spike-200.js", "cap001-failure-during-load.js" })
        {
            var text = System.IO.File.ReadAllText(RepoPaths.Resolve("perf", "k6", script));
            Assert.Contains("./lib/cap001.js", text);
            Assert.Contains("handleSummary", text);
        }
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "cap001-envelope-validate.ps1");
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{repoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(120_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
