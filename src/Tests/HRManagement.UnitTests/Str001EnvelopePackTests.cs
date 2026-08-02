using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Str001EnvelopePackTests
{
    private static readonly string[] RequiredFields =
        ["id", "value", "unit", "ownerRole", "asOfDate", "sourceTier", "envelope"];


    [Fact]
    public void Matrix_has_journeys_and_dual_envelopes()
    {
        var path = RepoPaths.Resolve("docs", "str-scope", "envelope-matrix.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("STR-001", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("NationalScope", journeys);
        Assert.Contains("QuantitativeEnvelope", journeys);
        Assert.Contains("WorkloadDataset", journeys);
        Assert.Contains("AssumptionsExclusions", journeys);
        Assert.Contains("GrowthReviewSignOff", journeys);

        var envs = root.GetProperty("indicators").EnumerateArray()
            .Select(i => i.GetProperty("envelope").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("national-competition", envs);
        Assert.Contains("syria-interim", envs);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .Select(b => b.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SYR-002", blockers);
        Assert.Contains("CAP-001", blockers);
    }

    [Fact]
    public void Indicators_have_required_fields_and_required_ids()
    {
        using var matrixDoc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-scope", "envelope-matrix.json")));
        using var reqDoc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str001", "required-indicators.json")));

        var byId = matrixDoc.RootElement.GetProperty("indicators").EnumerateArray()
            .ToDictionary(i => i.GetProperty("id").GetString()!, i => i, StringComparer.OrdinalIgnoreCase);

        foreach (var idEl in reqDoc.RootElement.GetProperty("requiredIndicatorIds").EnumerateArray())
        {
            var id = idEl.GetString()!;
            Assert.True(byId.ContainsKey(id), $"missing required indicator {id}");
            var ind = byId[id];
            foreach (var field in RequiredFields)
            {
                Assert.True(ind.TryGetProperty(field, out var prop), $"{id} missing {field}");
                if (prop.ValueKind == JsonValueKind.String)
                    Assert.False(string.IsNullOrWhiteSpace(prop.GetString()), $"{id} empty {field}");
            }
        }

        var syriaHasCensus = matrixDoc.RootElement.GetProperty("indicators").EnumerateArray()
            .Where(i => i.GetProperty("envelope").GetString() == "syria-interim")
            .Any(i => i.TryGetProperty("assumptionId", out var a) &&
                      a.GetString() == "ASS-SYR-CENSUS");
        Assert.True(syriaHasCensus, "syria-interim must link ASS-SYR-CENSUS");
    }

    [Fact]
    public void Floors_not_below_required_minima()
    {
        using var matrixDoc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-scope", "envelope-matrix.json")));
        using var reqDoc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str001", "required-indicators.json")));

        var byId = matrixDoc.RootElement.GetProperty("indicators").EnumerateArray()
            .ToDictionary(i => i.GetProperty("id").GetString()!, i => i, StringComparer.OrdinalIgnoreCase);

        foreach (var floor in reqDoc.RootElement.GetProperty("floors").EnumerateObject())
        {
            Assert.True(byId.ContainsKey(floor.Name), $"floor key missing in matrix: {floor.Name}");
            var val = byId[floor.Name].GetProperty("value");
            double num = val.ValueKind switch
            {
                JsonValueKind.Number => val.GetDouble(),
                JsonValueKind.String when double.TryParse(val.GetString(), out var parsed) => parsed,
                _ => double.NaN
            };
            Assert.False(double.IsNaN(num), $"{floor.Name} not numeric");
            Assert.True(num >= floor.Value.GetDouble(),
                $"{floor.Name} {num} below floor {floor.Value.GetDouble()}");
        }
    }

    [Fact]
    public void Exception_forbids_national_scale_without_pack()
    {
        var text = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "STR_001_EXCEPTION_PATHS.md"));
        Assert.Contains("national scale", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STR-001", text, StringComparison.Ordinal);
    }

    [Fact]
    public void File06_points_at_str001()
    {
        var text = System.IO.File.ReadAllText(RepoPaths.Resolve(
            "docs", "completion-backlog", "06-NATIONAL-READINESS-MODEL.md"));
        Assert.Contains("STR-001", text, StringComparison.Ordinal);
        Assert.Contains("envelope-matrix.json", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-001-national-scope-envelope.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_SCOPE_CHARTER.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_WORKLOAD_DATASET.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_ASSUMPTION_REGISTER.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_EXCLUSIONS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_GROWTH_2X_SCENARIO.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_INDEPENDENT_REVIEW.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "str001-envelope-validate.ps1")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "str001", "required-indicators.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str001-envelope-validate.ps1");
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
