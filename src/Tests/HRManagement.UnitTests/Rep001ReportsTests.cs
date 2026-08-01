using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Rep001ReportsTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "rep001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Catalog_card_is_rep001()
    {
        var path = RepoPaths.Resolve("docs", "rep", "statutory-catalog.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("REP-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Cross_tenant_forbidden()
    {
        var path = RepoPaths.Resolve("docs", "rep", "statutory-catalog.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("crossTenantForbidden").GetBoolean());
    }

    [Fact]
    public void Reports_have_golden_datasets_and_as_of()
    {
        var path = RepoPaths.Resolve("docs", "rep", "statutory-catalog.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var reports = doc.RootElement.GetProperty("reports").EnumerateArray().ToList();

        Assert.True(reports.Count >= 3);

        foreach (var rep in reports)
        {
            Assert.False(string.IsNullOrWhiteSpace(rep.GetProperty("reportId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rep.GetProperty("goldenDataset").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rep.GetProperty("legalAuthority").GetString()));
            Assert.True(rep.GetProperty("asOfRequired").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(rep.GetProperty("version").GetString()));
        }
    }

    [Fact]
    public void Metadata_policy_embeds_version_and_as_of()
    {
        var path = RepoPaths.Resolve("docs", "rep", "statutory-catalog.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var meta = doc.RootElement.GetProperty("metadataPolicy");

        Assert.True(meta.GetProperty("embedVersion").GetBoolean());
        Assert.True(meta.GetProperty("embedAsOf").GetBoolean());
        Assert.True(meta.GetProperty("embedSourceDataset").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REP_001_REPORTS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "rep", "statutory-catalog.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "rep001-validate.ps1");
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
