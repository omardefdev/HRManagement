using HRManagement.SharedKernel.Policy;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-49 (G-POL-02): docs/policy-rules-inventory.csv lists every static *Rules.cs file of the services with the policy domain
/// it belongs to and whether the engine governs that domain («محكوم») or the file's rules are the only rules («محلي»). The file
/// is written by scripts/policy-inventory.ps1 from the catalog; this test keeps it true: every rules file has its row, and the
/// status of every row is the catalog's. (The file had gone stale once: 31 rows for 64 files.)
/// </summary>
public sealed class Pol049RulesInventoryTests
{
    private const string Governed = "محكوم";
    private const string Local = "محلي";

    private static List<(string File, string Service, string Domain, string Status)> Rows()
    {
        var path = Path.Combine(RepoPaths.Root, "docs", "policy-rules-inventory.csv");
        var lines = global::System.IO.File.ReadAllLines(path).Select(l => l.TrimStart((char)0xFEFF)).Where(l => l.Length > 0).ToList();
        Assert.Equal($"\"File\",\"Service\",\"PolicyDomain\",\"{Governed}/{Local}\"", lines[0]);
        return lines.Skip(1).Select(line =>
        {
            var cells = line.Trim('"').Split("\",\"");
            Assert.True(cells.Length == 4, $"not four cells: {line}");
            return (cells[0], cells[1], cells[2], cells[3]);
        }).ToList();
    }

    [Fact]
    public void Every_static_rules_file_of_the_services_has_its_row()
    {
        var root = RepoPaths.Root;
        var files = Directory.EnumerateFiles(Path.Combine(root, "src", "Services"), "*Rules.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.True(files.Count >= 60, $"only {files.Count} rules files found");

        var listed = Rows().Select(r => r.File).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(files.SequenceEqual(listed, StringComparer.Ordinal),
            "docs/policy-rules-inventory.csv is not current. Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/policy-inventory.ps1\n" +
            "missing: " + string.Join(", ", files.Except(listed)) + "\nstale: " + string.Join(", ", listed.Except(files)));
    }

    [Fact]
    public void The_status_of_every_row_is_the_catalogs()
    {
        var rows = Rows();

        foreach (var row in rows)
        {
            Assert.Equal(row.File.Split('/')[2], row.Service);
            if (row.Domain.Length == 0)
            {
                // No policy domain at all: the rules are local by definition.
                Assert.Equal(Local, row.Status);
                continue;
            }

            var descriptor = PolicyDomainCatalog.Find(row.Domain);
            Assert.True(descriptor is not null, $"{row.File}: {row.Domain} is not a policy domain");
            Assert.Equal(descriptor!.IsGoverned ? Governed : Local, row.Status);
        }

        // Each governed domain whose service has rules files is there, and no row calls an out-of-scope domain governed.
        var governedInFile = rows.Where(r => r.Status == Governed).Select(r => r.Domain).Distinct(StringComparer.Ordinal).ToList();
        Assert.All(governedInFile, domain => Assert.True(PolicyDomainCatalog.IsGoverned(domain)));
        Assert.Contains(PolicyDomains.AssetsCustody, governedInFile);
        Assert.Contains(PolicyDomains.ContractsLeave, governedInFile);
        Assert.Contains(PolicyDomains.CoreRecruitment, governedInFile);
        Assert.Contains(PolicyDomains.CoreEstablishment, governedInFile);
        // One file, two domains: leave is governed, advances are not.
        var leaveAndAdvance = rows.Where(r => r.File.EndsWith("/LeaveAndAdvanceRules.cs", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, leaveAndAdvance.Count);
        Assert.Contains(leaveAndAdvance, r => r.Domain == PolicyDomains.ContractsLeave && r.Status == Governed);
        Assert.Contains(leaveAndAdvance, r => r.Domain == PolicyDomains.ContractsAdvance && r.Status == Local);
    }
}
