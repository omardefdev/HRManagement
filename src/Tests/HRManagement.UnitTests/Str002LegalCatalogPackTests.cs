using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Str002LegalCatalogPackTests
{
    private static readonly string[] RequiredFields =
    [
        "requirementId", "domain", "title", "sourceRef", "confidence",
        "cardIds", "controlOwner", "evidencePath", "testRef", "frequency", "status"
    ];


    [Fact]
    public void Catalog_has_journeys_and_ten_domains()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-legal", "control-catalog.json")));
        using var req = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str002", "required-domains.json")));

        var root = doc.RootElement;
        Assert.Equal("STR-002", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DomainCoverage", journeys);
        Assert.Contains("ControlLinkage", journeys);
        Assert.Contains("ConflictExceptions", journeys);
        Assert.Contains("LegislativeWatch", journeys);
        Assert.Contains("TraceWalkthrough", journeys);

        var domains = root.GetProperty("controls").EnumerateArray()
            .Select(c => c.GetProperty("domain").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var d in req.RootElement.GetProperty("requiredDomains").EnumerateArray())
            Assert.Contains(d.GetString()!, domains);
    }

    [Fact]
    public void Controls_have_required_fields_and_legal_tbd_rule()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-legal", "control-catalog.json")));
        var exceptions = doc.RootElement.GetProperty("exceptions").EnumerateArray()
            .Select(e => e.GetProperty("exceptionId").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var c in doc.RootElement.GetProperty("controls").EnumerateArray())
        {
            var id = c.GetProperty("requirementId").GetString();
            foreach (var field in RequiredFields)
            {
                Assert.True(c.TryGetProperty(field, out var prop), $"{id} missing {field}");
                if (field == "cardIds")
                    Assert.True(prop.GetArrayLength() >= 1, $"{id} empty cardIds");
                else if (prop.ValueKind == JsonValueKind.String)
                    Assert.False(string.IsNullOrWhiteSpace(prop.GetString()), $"{id} empty {field}");
            }

            var status = c.GetProperty("status").GetString()!;
            if (status.Equals("Implemented", StringComparison.OrdinalIgnoreCase))
            {
                var blob = $"{c.GetProperty("evidencePath").GetString()} {c.GetProperty("testRef").GetString()}";
                Assert.DoesNotContain("LEGAL" + "_TBD", blob, StringComparison.OrdinalIgnoreCase);
            }

            var confidence = c.GetProperty("confidence").GetString()!;
            if (confidence == "D")
            {
                var hasEx = c.TryGetProperty("exceptionId", out var ex) &&
                            ex.ValueKind == JsonValueKind.String &&
                            exceptions.Contains(ex.GetString()!);
                Assert.True(status.Equals("Blocked-Legal", StringComparison.OrdinalIgnoreCase) || hasEx,
                    $"{id} confidence D needs Blocked-Legal or exception");
            }
        }
    }

    [Fact]
    public void Trace_samples_have_at_least_three_traces()
    {
        var text = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "STR_002_TRACE_SAMPLES.md"));
        var count = Regex.Matches(text, @"(?m)^## Trace \d+").Count;
        Assert.True(count >= 3, $"expected >=3 Trace headings, got {count}");
    }

    [Fact]
    public void Gov002_verification_points_at_str002()
    {
        var text = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "GOV_002_VERIFICATION.md"));
        Assert.Contains("STR-002", text, StringComparison.Ordinal);
        Assert.Contains("control-catalog.json", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-002-legal-control-catalog.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_DOMAIN_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_CONFLICT_EXCEPTION_REGISTER.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_LEGISLATIVE_WATCH.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_RELEASE_GATE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_TRACE_SAMPLES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "str002-control-catalog-validate.ps1")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "str002", "required-domains.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str002-control-catalog-validate.ps1");
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
