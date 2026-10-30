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
        // QA-034 (D-QA-12): the committed global.json is checked (git show HEAD:global.json), so a local rollForward
        // edit of the working-tree file does not turn this test red; CI and every clone see the committed value.
        var script = RepoPaths.Resolve("scripts", "san001-validate.ps1");
        var (exitCode, stdout, stderr) = RepoScriptRunner.RunScript(script);
        Assert.True(exitCode == 0, $"validate failed ({exitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SDK pin read from git show HEAD:global.json", stdout, StringComparison.Ordinal);
    }
}
