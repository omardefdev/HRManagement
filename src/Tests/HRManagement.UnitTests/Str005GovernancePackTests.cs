using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Str005GovernancePackTests
{
    // QA-034: governance dates are yyyy-MM-dd; the thread culture (ar-SA, Hijri) must not change the year.
    private static DateTime ParseIsoDate(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Matrix_has_journeys_boards_rights_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-governance", "governance-matrix.json")));

        var root = doc.RootElement;
        Assert.Equal("STR-005", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DecisionRights", journeys);
        Assert.Contains("BoardsAndAdr", journeys);
        Assert.Contains("ExceptionExpiry", journeys);
        Assert.Contains("GoLiveMultiSeat", journeys);

        var boards = root.GetProperty("boards").EnumerateArray()
            .Select(b => b.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Architecture", boards);
        Assert.Contains("Security", boards);
        Assert.Contains("Data", boards);
        Assert.Contains("Change", boards);
        Assert.Contains("Program", boards);

        foreach (var right in root.GetProperty("decisionRights").EnumerateArray())
        {
            var owner = right.GetProperty("ownerRole").GetString();
            var reviewer = right.GetProperty("reviewerRole").GetString();
            Assert.False(string.IsNullOrWhiteSpace(owner));
            Assert.False(string.IsNullOrWhiteSpace(reviewer));
            Assert.NotEqual(owner, reviewer);
        }

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Blocked", blockers["SYD-017"]);
        Assert.Equal("Satisfied", blockers["PGM-001"]);
    }

    [Fact]
    public void Decision_log_owner_differs_from_reviewer()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-governance", "decision-log.json")));
        Assert.Equal("STR-005", doc.RootElement.GetProperty("card").GetString());
        Assert.True(doc.RootElement.GetProperty("decisions").GetArrayLength() >= 3);
        foreach (var d in doc.RootElement.GetProperty("decisions").EnumerateArray())
        {
            Assert.NotEqual(
                d.GetProperty("ownerRole").GetString(),
                d.GetProperty("reviewerRole").GetString());
        }
    }

    [Fact]
    public void Exceptions_have_expiry_and_are_not_past_asOf()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-governance", "governance-matrix.json")));
        var asOf = ParseIsoDate(doc.RootElement.GetProperty("asOfDate").GetString()!);
        foreach (var ex in doc.RootElement.GetProperty("exceptions").EnumerateArray())
        {
            var expires = ParseIsoDate(ex.GetProperty("expiresOn").GetString()!);
            var status = ex.GetProperty("status").GetString();
            if (status is "Renewed" or "Closed")
                continue;
            Assert.True(expires.Date >= asOf.Date, $"{ex.GetProperty("exceptionId").GetString()} expired");
            Assert.NotEqual(
                ex.GetProperty("declaredBy").GetString(),
                ex.GetProperty("reviewerRole").GetString());
        }
    }

    [Fact]
    public void GoLive_eng_only_fails_multi_seat_passes()
    {
        using var matrix = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-governance", "governance-matrix.json")));
        var required = matrix.RootElement.GetProperty("goLiveMultiSeat").GetProperty("requiredSeats")
            .EnumerateArray().Select(s => s.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var engAliases = matrix.RootElement.GetProperty("goLiveMultiSeat").GetProperty("engRoleAliases")
            .EnumerateArray().Select(s => s.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var eng = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str005", "go-live", "eng-only.json")));
        using var multi = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str005", "go-live", "multi-seat.json")));

        Assert.Equal("FAIL", eng.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", multi.RootElement.GetProperty("expectedValidate").GetString());

        static bool MultiSeatOk(JsonElement seatsEl, HashSet<string> required, HashSet<string> engAliases)
        {
            var seats = seatsEl.EnumerateArray().Select(s => s.GetString()!).ToList();
            if (seats.Count == 0 || seats.All(s => engAliases.Contains(s)))
                return false;
            return required.All(r => seats.Any(s => string.Equals(s, r, StringComparison.OrdinalIgnoreCase)));
        }

        Assert.False(MultiSeatOk(eng.RootElement.GetProperty("seats"), required, engAliases));
        Assert.True(MultiSeatOk(multi.RootElement.GetProperty("seats"), required, engAliases));
    }

    [Fact]
    public void Simulation_scenarios_exist_with_owner_reviewer()
    {
        foreach (var name in new[] { "law-change.json", "incident.json", "delay.json" })
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
                RepoPaths.Resolve("testdata", "str005", "scenarios", name)));
            Assert.NotEqual(
                doc.RootElement.GetProperty("ownerRole").GetString(),
                doc.RootElement.GetProperty("reviewerRole").GetString());
            Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("decisionId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("riskId").GetString()));
        }
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-005-program-governance-boards.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_DECISION_RIGHTS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_DECISION_LOG.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_RISK_REGISTER.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_CONFLICT_ESCALATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_MILESTONE_EVIDENCE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_SIMULATION_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_GO_LIVE_GATE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "str-governance", "decision-log.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "str-governance", "risk-register.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "str005-governance-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str005-governance-validate.ps1");
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
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
