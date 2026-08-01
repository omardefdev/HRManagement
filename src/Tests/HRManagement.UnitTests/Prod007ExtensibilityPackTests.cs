using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Prod007ExtensibilityPackTests
{

    [Fact]
    public void Matrix_has_extensibility_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-extensibility", "extensibility-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("PROD-007", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("compatibilityWindows").GetProperty("supportNMinus2").GetBoolean());
        Assert.True(root.GetProperty("pluginIsolation").GetProperty("crossTenantForbidden").GetBoolean());
        Assert.True(root.GetProperty("sdkPolicy").GetProperty("liveSdkForbidden").GetBoolean());
        Assert.True(root.GetProperty("noDirectDb").GetBoolean());
        Assert.True(root.GetProperty("noCustomFork").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveExtensionRuntimeForbidden").GetBoolean());

        var types = root.GetProperty("supportedContracts").GetProperty("contractTypes").EnumerateArray()
            .Select(s => s.GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in new[] { "apis", "events", "webhooks" })
            Assert.Contains(need, types);

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("VersionCompat", journeys);
        Assert.Contains("IsolationQuotas", journeys);
        Assert.Contains("DeprecationUpgrade", journeys);
        Assert.Contains("NoForkDb", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["INT-002"]);
        Assert.Equal("Satisfied", blockers["SUP-001"]);
        Assert.Equal("Satisfied", blockers["STR-004"]);
        Assert.Equal("Blocked", blockers["live-extension-runtime"]);
        Assert.Equal("Blocked", blockers["live-partner-sdk"]);
        Assert.Equal("Blocked", blockers["live-webhook-delivery"]);
        Assert.Equal("NotStarted", blockers["EXT-006"]);
        Assert.Equal("NotStarted", blockers["IOP-001"]);
        Assert.Equal("NotStarted", blockers["REL-004"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-007-extensibility-platform.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_007_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_007_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod007", "scenarios", "n2-break-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod007", "harness", "extensibility-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "prod007-extensibility-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod007-extensibility-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "prod-extensibility", "extensibility-matrix.json");
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
