using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Sov003KeySovereigntyPackTests
{

    [Fact]
    public void Matrix_has_key_sovereignty_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-keys", "key-sovereignty-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("SOV-003", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("hsmKms").GetProperty("vendorAloneCannotDecrypt").GetBoolean());
        Assert.True(root.GetProperty("dualControl").GetProperty("required").GetBoolean());
        Assert.True(root.GetProperty("rotation").GetProperty("custodianLossDrillRequired").GetBoolean());
        Assert.True(root.GetProperty("trustedTime").GetProperty("clockDriftBoundRequired").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveHsmForbidden").GetBoolean());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("HierarchyCustody", journeys);
        Assert.Contains("ControlRotation", journeys);
        Assert.Contains("AgilityTime", journeys);
        Assert.Contains("ClaimGate", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["OPS-002"]);
        Assert.Equal("Satisfied", blockers["STR-002"]);
        Assert.Equal("Satisfied", blockers["SOV-002"]);
        Assert.Equal("Blocked", blockers["SEC-004"]);
        Assert.Equal("Blocked", blockers["live-hsm-prod"]);
        Assert.Equal("Satisfied", blockers["EXT-004"]);
        Assert.Equal("Satisfied", blockers["SOV-004"]);
        Assert.Equal("NotStarted", blockers["SIG-001"]);
        Assert.Equal("NotStarted", blockers["NAT-001"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-003-key-sovereignty.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_003_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_003_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov003", "scenarios", "vendor-alone-decrypt-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "sov003", "harness", "key-sovereignty-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "sov003-key-sovereignty-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov003-key-sovereignty-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "sov-keys", "key-sovereignty-matrix.json");
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
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
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"pack validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
