using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>ONB-001: tests for agency provisioning manifest artifacts.</summary>
public class Onb001ProvisioningTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "onb001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Manifest_schema_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "onb", "agency-manifest-schema.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("ONB-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Manifest_schema_forbids_secrets_and_manual_db_edits()
    {
        var path = RepoPaths.Resolve("docs", "onb", "agency-manifest-schema.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var rules = doc.RootElement.GetProperty("provisioningRules");
        Assert.True(rules.GetProperty("noSecretInPack").GetBoolean());
        Assert.True(rules.GetProperty("noManualDbEdits").GetBoolean());
        Assert.True(rules.GetProperty("rollbackSupported").GetBoolean());
    }

    [Fact]
    public void Example_manifest_has_four_eyes_reviewers()
    {
        var path = RepoPaths.Resolve("docs", "onb", "agency-manifest-schema.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var reviewers = doc.RootElement.GetProperty("exampleManifest").GetProperty("reviewerRoles");
        Assert.True(reviewers.GetArrayLength() >= 2);
    }

    [Fact]
    public void Example_manifest_preserves_national_mandatory_controls()
    {
        var path = RepoPaths.Resolve("docs", "onb", "agency-manifest-schema.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var policies = doc.RootElement.GetProperty("exampleManifest").GetProperty("policies");
        Assert.True(policies.GetProperty("preserveNationalMandatory").GetBoolean());
    }

    [Fact]
    public void Schema_requires_core_provisioning_sections()
    {
        var path = RepoPaths.Resolve("docs", "onb", "agency-manifest-schema.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var required = doc.RootElement.GetProperty("required").EnumerateArray()
            .Select(r => r.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var field in new[] { "tenant", "identity", "roles", "policies", "retention", "backup" })
            Assert.Contains(field, required);
    }

    [Fact]
    public void Doc_exists_and_addresses_provisioning_rules()
    {
        var path = RepoPaths.Resolve("docs", "ONB_001_PROVISIONING.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("ONB-001", content);
        Assert.Contains("manifest", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secret", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ONB_001_PROVISIONING.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "onb", "agency-manifest-schema.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "onb001-validate.ps1");
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
