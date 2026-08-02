using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Prod005WorkboxPackTests
{

    [Fact]
    public void Matrix_has_workbox_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-workbox", "workbox-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("PROD-005", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("queueCompleteness").GetProperty("noLostCrossModule").GetBoolean());
        Assert.True(root.GetProperty("safeSearch").GetProperty("existenceLeakForbidden").GetBoolean());
        Assert.True(root.GetProperty("bulkActions").GetProperty("fourEyesRequired").GetBoolean());
        Assert.True(root.GetProperty("notifications").GetProperty("liveForbidden").GetBoolean());
        Assert.True(root.GetProperty("deepLinks").GetProperty("reauthOnExpiredRequired").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveWorkboxForbidden").GetBoolean());

        var itemTypes = root.GetProperty("queueCompleteness").GetProperty("itemTypes").EnumerateArray()
            .Select(s => s.GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in new[] { "tasks", "cases", "approvals" })
            Assert.Contains(need, itemTypes);

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("QueueCompleteness", journeys);
        Assert.Contains("SafeSearch", journeys);
        Assert.Contains("BulkFourEyes", journeys);
        Assert.Contains("NotifyDeepLink", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["INT-003"]);
        Assert.Equal("Satisfied", blockers["EXT-007"]);
        Assert.Equal("NotStarted", blockers["IAM-002"]);
        Assert.Equal("Blocked", blockers["live-workbox-ui"]);
        Assert.Equal("Blocked", blockers["live-notification-delivery"]);
        Assert.Equal("Blocked", blockers["live-safe-search"]);
        Assert.Equal("NotStarted", blockers["COM-001"]);
        Assert.Equal("NotStarted", blockers["WF-002"]);
        Assert.Equal("Satisfied", blockers["PROD-006"]);
        Assert.Equal("NotStarted", blockers["NAT-005"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-005-workbox.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_005_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_005_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod005", "scenarios", "lost-transaction-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod005", "harness", "workbox-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "prod005-workbox-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod005-workbox-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "prod-workbox", "workbox-matrix.json");
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
