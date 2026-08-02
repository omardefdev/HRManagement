using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Iop001SemanticTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "iop001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Canonical_identifiers_are_valid()
    {
        var path = RepoPaths.Resolve("docs", "iop", "canonical-identifiers.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("IOP-001", root.GetProperty("card").GetString());
        var ids = root.GetProperty("identifiers").EnumerateArray().ToList();
        Assert.True(ids.Count >= 3, "must define at least 3 canonical identifiers");

        foreach (var id in ids)
        {
            Assert.False(string.IsNullOrWhiteSpace(id.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(id.GetProperty("owner").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(id.GetProperty("type").GetString()));
        }

        var names = ids.Select(i => i.GetProperty("name").GetString()).ToHashSet();
        Assert.Contains("PersonId", names);
        Assert.Contains("EmployeeId", names);
        Assert.Contains("TenantId", names);
    }

    [Fact]
    public void Schema_registry_profiles_are_valid()
    {
        var path = RepoPaths.Resolve("docs", "iop", "schema-registry-profile.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("IOP-001", root.GetProperty("card").GetString());
        var profiles = root.GetProperty("profiles").EnumerateArray().ToList();
        Assert.True(profiles.Count >= 2, "must define at least 2 profiles");

        foreach (var p in profiles)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("partner").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("schemaFormat").GetString()));
        }
    }

    [Fact]
    public void Canonical_identifiers_have_conventions()
    {
        var path = RepoPaths.Resolve("docs", "iop", "canonical-identifiers.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        var conventions = root.GetProperty("conventions");
        Assert.False(string.IsNullOrWhiteSpace(conventions.GetProperty("dateTime").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(conventions.GetProperty("currency").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(conventions.GetProperty("pagination").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(conventions.GetProperty("errors").GetString()));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "iop001-validate.ps1");
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

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "IOP_001_SEMANTIC_MODEL.md")));
    }
}
