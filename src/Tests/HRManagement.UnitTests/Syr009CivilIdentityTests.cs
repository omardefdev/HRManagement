using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>SYR-009: tests for civil registry identity match rules structure.</summary>
public class Syr009CivilIdentityTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr009-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Rules_card_is_syr009_and_extends_ext002()
    {
        var path = RepoPaths.Resolve("docs", "syr", "civil-identity-match-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("SYR-009", root.GetProperty("card").GetString());
        Assert.Equal("EXT-002", root.GetProperty("extendsCard").GetString());
    }

    [Fact]
    public void Official_identifier_is_national_id()
    {
        var path = RepoPaths.Resolve("docs", "syr", "civil-identity-match-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var id = doc.RootElement.GetProperty("officialIdentifier");

        Assert.Equal("nationalId", id.GetProperty("field").GetString());
        Assert.Equal(11, id.GetProperty("length").GetInt32());
    }

    [Fact]
    public void Match_rules_have_confidence_levels()
    {
        var path = RepoPaths.Resolve("docs", "syr", "civil-identity-match-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var rules = doc.RootElement.GetProperty("matchRules").EnumerateArray().ToList();
        Assert.True(rules.Count >= 3);

        var confidences = rules.Select(r => r.GetProperty("confidence").GetString()).ToList();
        Assert.Contains("high", confidences);
        Assert.Contains("medium", confidences);
    }

    [Fact]
    public void Auto_merge_is_forbidden_for_duplicates()
    {
        var path = RepoPaths.Resolve("docs", "syr", "civil-identity-match-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("deduplication")
            .GetProperty("autoMergeForbidden").GetBoolean());
    }

    [Fact]
    public void Outage_forbids_permanent_identity_without_review()
    {
        var path = RepoPaths.Resolve("docs", "syr", "civil-identity-match-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("outagePolicy")
            .GetProperty("forbidPermanentIdentityWithoutReview").GetBoolean());
    }

    [Fact]
    public void Arabic_name_rules_use_normalization()
    {
        var path = RepoPaths.Resolve("docs", "syr", "civil-identity-match-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var nameRules = doc.RootElement.GetProperty("arabicNameRules");

        Assert.True(nameRules.GetProperty("matchOnNormalizedForm").GetBoolean());
        Assert.True(nameRules.GetProperty("preserveOriginal").GetBoolean());
        Assert.Contains("L10N-002", nameRules.GetProperty("normalizationVersion").GetString());
    }

    [Fact]
    public void Audit_is_required_with_retention()
    {
        var path = RepoPaths.Resolve("docs", "syr", "civil-identity-match-rules.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var audit = doc.RootElement.GetProperty("audit");

        Assert.True(audit.GetProperty("required").GetBoolean());
        Assert.True(audit.GetProperty("retentionDays").GetInt32() > 0);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_009_CIVIL_REGISTRY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "civil-identity-match-rules.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr009-validate.ps1");
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
