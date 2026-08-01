using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Rel002PilotGatePackTests
{

    private static string ResolveRepoDir(params string[] parts)
    {
        var file = RepoPaths.Resolve(parts.Append("cycles.json").ToArray());
        return Path.GetDirectoryName(file)!;
    }

    [Fact]
    public void Scope_matrix_has_journeys_and_blocker_enum()
    {
        var path = RepoPaths.Resolve("docs", "rel-pilot", "scope-matrix.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("REL-002", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PilotScope", journeys);
        Assert.Contains("EvidenceGates", journeys);
        Assert.Contains("GoNoGoDecision", journeys);

        var allowed = root.GetProperty("allowedBlockerStatuses").EnumerateArray()
            .Select(x => x.GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Blocked", allowed);
        Assert.Contains("Waived", allowed);
        Assert.Contains("Satisfied", allowed);

        foreach (var b in root.GetProperty("blockers").EnumerateArray())
        {
            var status = b.GetProperty("status").GetString();
            Assert.True(allowed.Contains(status),
                $"blocker {b.GetProperty("id").GetString()} status {status} not in enum");
        }

        Assert.Equal("PILOT-AGENCY-001", root.GetProperty("pilotCharter").GetProperty("agencyId").GetString());
    }

    [Fact]
    public void Payroll_parallel_has_three_cycles_and_policy()
    {
        var path = RepoPaths.Resolve("testdata", "rel002", "payroll-parallel", "cycles.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("REL-002", root.GetProperty("card").GetString());
        Assert.Equal(3, root.GetProperty("policy").GetProperty("cycleCount").GetInt32());
        Assert.True(root.GetProperty("policy").GetProperty("forbidLiveAgencyPayroll").GetBoolean());
        Assert.True(root.GetProperty("policy").GetProperty("unexplainedDiffFails").GetBoolean());
        Assert.Equal(3, root.GetProperty("cycles").GetArrayLength());
    }

    [Fact]
    public void Explained_diffs_require_reason_code()
    {
        var baseDir = ResolveRepoDir("testdata", "rel002", "payroll-parallel");
        foreach (var file in Directory.GetFiles(Path.Combine(baseDir, "explained"), "*.json"))
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
            foreach (var d in doc.RootElement.GetProperty("diffs").EnumerateArray())
            {
                var code = d.GetProperty("reasonCode").GetString();
                Assert.False(string.IsNullOrWhiteSpace(code), $"{file}: empty reasonCode");
            }
        }
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-REL-002-controlled-pilot.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_PILOT_REPORT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_DEFECT_LEAKAGE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_DAILY_RECONCILIATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_SLO_WINDOW.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_SUPPORT_CONTACTS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_DR_SECURITY_BINDER.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_MIGRATION_REHEARSAL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_PILOT_GO_NO_GO.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "rel-pilot", "defect-leakage-template.csv")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "rel002-payroll-parallel-compare.ps1")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "rel002", "payroll-parallel", "explained-diff.schema.json")));
    }

    [Fact]
    public void Go_no_go_doc_has_multi_role_and_pilot_only()
    {
        var text = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "REL_002_PILOT_GO_NO_GO.md"));
        Assert.Contains("Program", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Security", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Privacy", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pilot only", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not grant national ATO", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Payroll_compare_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "rel002-payroll-parallel-compare.ps1");
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
        Assert.True(proc.ExitCode == 0, $"compare failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
