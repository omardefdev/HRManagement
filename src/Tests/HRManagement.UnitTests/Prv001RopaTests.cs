using System.Text.Json;

namespace HRManagement.UnitTests;

public class Prv001RopaTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "prv-ropa", "processing-activities.json");
            if (System.IO.File.Exists(candidate))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Ropa_covers_all_fourteen_services_and_high_risk_dpias()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "prv-ropa", "processing-activities.json")));
        var root = doc.RootElement;
        Assert.Equal("PRV-001", root.GetProperty("card").GetString());

        var services = root.GetProperty("activities").EnumerateArray()
            .Select(a => a.GetProperty("service").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var need in new[]
                 {
                     "HR.Core", "Payroll", "HR.Health", "HR.Contracts", "HR.Movements", "HR.Training",
                     "IDCard", "File.Storage", "Compliance", "Dev.Analytics", "IT.Infra",
                     "Auth.Directory", "Admin.Logistics", "Records.Archive"
                 })
        {
            Assert.Contains(need, services);
        }

        foreach (var a in root.GetProperty("activities").EnumerateArray())
        {
            if (a.GetProperty("highRisk").GetBoolean())
            {
                var dpia = a.GetProperty("dpiaRef").GetString();
                Assert.False(string.IsNullOrWhiteSpace(dpia));
                Assert.True(System.IO.File.Exists(Path.Combine(ResolveRepoRoot(), "docs", dpia!)), dpia);
            }
        }
    }

    [Fact]
    public void Sharing_and_subprocessor_docs_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "PRV_001_DATA_SHARING.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "PRV_001_SUBPROCESSOR_REGISTER.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "PRV_001_ROPA.md")));
    }
}
