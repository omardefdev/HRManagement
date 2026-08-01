using System.Text.Json;

namespace HRManagement.UnitTests.Architecture;

/// <summary>UX-001: production hosts must enforce CSP (not Report-Only).</summary>
public class CspProductionConfigTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !global::System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
        }
    }

    [Fact]
    public void All_production_appsettings_enforce_csp()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src"), "appsettings.Production.json", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(files);

        var missing = new List<string>();
        foreach (var file in files)
        {
            using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("Security", out var security)
                || !security.TryGetProperty("CspMode", out var mode)
                || !string.Equals(mode.GetString(), "Enforce", StringComparison.OrdinalIgnoreCase))
            {
                missing.Add(Path.GetRelativePath(RepoRoot, file));
            }
        }

        Assert.True(
            missing.Count == 0,
            "Security:CspMode must be Enforce in all appsettings.Production.json. Missing:\n"
            + string.Join("\n", missing));
    }
}
