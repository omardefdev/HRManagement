using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>IOP-002: tests for data portability artifacts and export manifest schema.</summary>
public class Iop002PortabilityTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "iop002-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Export_manifest_schema_has_required_fields()
    {
        var path = RepoPaths.Resolve("docs", "iop", "export-manifest-schema.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("IOP-002", root.GetProperty("card").GetString());

        var required = root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.Contains("manifestVersion", required);
        Assert.Contains("exportType", required);
        Assert.Contains("tenantId", required);
        Assert.Contains("entities", required);
        Assert.Contains("controlTotals", required);
        Assert.Contains("checksums", required);
    }

    [Fact]
    public void Export_manifest_defines_entities_structure()
    {
        var path = RepoPaths.Resolve("docs", "iop", "export-manifest-schema.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var props = doc.RootElement.GetProperty("properties");

        Assert.True(props.TryGetProperty("entities", out var entities));
        Assert.Equal("array", entities.GetProperty("type").GetString());

        var itemRequired = entities.GetProperty("items")
            .GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.Contains("entityType", itemRequired);
        Assert.Contains("recordCount", itemRequired);
        Assert.Contains("checksumSha256", itemRequired);
    }

    [Fact]
    public void Export_manifest_defines_control_totals()
    {
        var path = RepoPaths.Resolve("docs", "iop", "export-manifest-schema.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var controlTotals = doc.RootElement.GetProperty("properties").GetProperty("controlTotals");

        var required = controlTotals.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.Contains("totalRecords", required);
        Assert.Contains("entityCounts", required);
    }

    [Fact]
    public void Export_manifest_defines_checksum_integrity()
    {
        var path = RepoPaths.Resolve("docs", "iop", "export-manifest-schema.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var checksums = doc.RootElement.GetProperty("properties").GetProperty("checksums");

        Assert.Equal("SHA-256",
            checksums.GetProperty("properties").GetProperty("algorithm")
                .GetProperty("const").GetString());
    }

    [Fact]
    public void Export_manifest_supports_identity_mapping()
    {
        var path = RepoPaths.Resolve("docs", "iop", "export-manifest-schema.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var props = doc.RootElement.GetProperty("properties");

        Assert.True(props.TryGetProperty("identityMapping", out var mapping));
        var strategy = mapping.GetProperty("properties").GetProperty("strategy");
        var enumValues = strategy.GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.Contains("preserve", enumValues);
        Assert.Contains("remap", enumValues);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "IOP_002_PORTABILITY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "iop", "export-manifest-schema.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "iop002-validate.ps1");
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
