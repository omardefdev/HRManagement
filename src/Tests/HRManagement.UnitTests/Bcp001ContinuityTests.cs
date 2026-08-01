using System.Text.Json;

namespace HRManagement.UnitTests;

/// <summary>BCP-001: business continuity manual procedures, degraded matrix, drill, re-entry.</summary>
public class Bcp001ContinuityTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docker-compose.production.yml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Read(string relative) =>
        System.IO.File.ReadAllText(Path.Combine(ResolveRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Bcp001_docs_scripts_and_matrix_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/BCP_001_MANUAL_PROCEDURES.md",
                     "docs/BCP_001_REENTRY_RECONCILIATION.md",
                     "docs/BCP_001_DRILL_PLAYBOOK.md",
                     "docs/bcp/degraded-mode-matrix.json",
                     "docs/bcp/degraded-mode-triggers.json",
                     "scripts/bcp001-validate.ps1"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }

    [Fact]
    public void Degraded_mode_matrix_covers_tier0_journeys()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "bcp", "degraded-mode-matrix.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("BCP-001", root.GetProperty("cardId").GetString());

        var ids = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var must in new[] { "payroll-disbursement", "identity-access", "emergency-leave", "attendance-correction" })
            Assert.Contains(must, ids);

        var tier0 = root.GetProperty("journeys").EnumerateArray()
            .Where(j => j.GetProperty("tier").GetInt32() == 0)
            .ToList();
        Assert.True(tier0.Count >= 3);
        foreach (var j in tier0)
            Assert.True(j.GetProperty("rtoHours").GetInt32() <= 4);
    }

    [Fact]
    public void Manual_procedures_cover_payroll_identity_attendance_leave()
    {
        var doc = Read("docs/BCP_001_MANUAL_PROCEDURES.md");
        Assert.Contains("Tier-0", doc);
        Assert.Contains("Payroll", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Identity", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Attendance", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Leave", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Drill_playbook_defines_8h_exercise()
    {
        var doc = Read("docs/BCP_001_DRILL_PLAYBOOK.md");
        Assert.Contains("8", doc);
        Assert.Contains("hour", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reentry_doc_covers_duplicate_detection()
    {
        var doc = Read("docs/BCP_001_REENTRY_RECONCILIATION.md");
        Assert.Contains("duplicate", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ManualEntrySource=BCP", doc);
    }
}
