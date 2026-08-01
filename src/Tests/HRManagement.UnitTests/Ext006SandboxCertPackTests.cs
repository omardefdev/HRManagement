using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Ext006SandboxCertPackTests
{

    [Fact]
    public void Matrix_has_sandbox_cert_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-sandbox", "sandbox-cert-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("EXT-006", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("syntheticDataOnly").GetBoolean());
        Assert.True(root.GetProperty("realPiiForbidden").GetBoolean());
        Assert.True(root.GetProperty("credentials").GetProperty("noProdAccess").GetBoolean());
        Assert.True(root.GetProperty("conformance").GetProperty("incompatibleClientMustFail").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("livePortalForbidden").GetBoolean());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PartnerOnboard", journeys);
        Assert.Contains("SyntheticOnly", journeys);
        Assert.Contains("ConformanceGate", journeys);
        Assert.Contains("CertificationRevocation", journeys);
        Assert.Contains("DocsMatch", journeys);
        Assert.Contains("RateErrorSim", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["EXT-005"]);
        Assert.Equal("Satisfied", blockers["REL-001"]);
        Assert.Equal("Blocked", blockers["live-developer-portal"]);
        Assert.Equal("Blocked", blockers["real-pii-in-sandbox"]);
        Assert.Equal("Blocked", blockers["partner-cert-fleet-live"]);
        Assert.Equal("Satisfied", blockers["EXT-007"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-006-sandbox-cert-developer-portal.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_006_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_006_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "ext006", "scenarios", "prod-access-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "ext006", "harness", "sandbox-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "ext006-sandbox-cert-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext006-sandbox-cert-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "ext-sandbox", "sandbox-cert-matrix.json");
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
