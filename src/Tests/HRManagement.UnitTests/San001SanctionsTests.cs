using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class San001SanctionsTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "san001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    private static readonly string[] ValidLegalStatuses =
        ["available", "restricted", "unpayable", "geo-blocked"];

    [Fact]
    public void Classification_card_is_san001()
    {
        var path = RepoPaths.Resolve("docs", "san", "dependency-classification.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("SAN-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Dependencies_are_classified_with_valid_status()
    {
        var path = RepoPaths.Resolve("docs", "san", "dependency-classification.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var deps = doc.RootElement.GetProperty("dependencies").EnumerateArray().ToList();

        Assert.True(deps.Count >= 5);

        foreach (var dep in deps)
        {
            var status = dep.GetProperty("legalStatus").GetString();
            Assert.Contains(status, ValidLegalStatuses);
            Assert.False(string.IsNullOrWhiteSpace(dep.GetProperty("owner").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(dep.GetProperty("criticality").GetString()));
        }
    }

    [Fact]
    public void Critical_dependencies_have_alternatives()
    {
        var path = RepoPaths.Resolve("docs", "san", "dependency-classification.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var critical = doc.RootElement.GetProperty("dependencies").EnumerateArray()
            .Where(d => d.GetProperty("criticality").GetString() == "critical");

        foreach (var dep in critical)
        {
            Assert.True(
                dep.TryGetProperty("alternative", out var alt) && !string.IsNullOrWhiteSpace(alt.GetString()),
                $"Critical dependency {dep.GetProperty("dependencyId").GetString()} must have alternative");
        }
    }

    [Fact]
    public void Mirror_and_pinning_policy_defined()
    {
        var path = RepoPaths.Resolve("docs", "san", "dependency-classification.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("mirrorPolicy", out _));
        Assert.True(root.TryGetProperty("pinningPolicy", out var pin));
        Assert.Equal("global.json", pin.GetProperty("sdkPin").GetString());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SAN_001_SANCTIONS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "san", "dependency-classification.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "san001-validate.ps1");
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
