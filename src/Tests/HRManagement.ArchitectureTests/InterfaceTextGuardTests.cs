using HRManagement.TestSupport;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-67 (§9, P-12, G-UX-10): every Razor view of the repository is read by <see cref="InterfaceTextScanner"/>, and none may show
/// an English label, a raw status, a printed id, raw JSON, an inline handler, a style attribute, a looped table without an empty
/// message, or a date or time formatted outside the time helpers. A finding is allowed only as a row of its project's ceiling list,
/// <c>tests/architecture/ui-text-allowlist/&lt;project&gt;.txt</c> (<see cref="CeilingList"/>): the lists only shrink. The technical
/// exceptions are not rows: they are named, with their reasons, in <see cref="InterfaceTextScanner.TechnicalIds"/>.
/// </summary>
public sealed class InterfaceTextGuardTests
{
    public const string ListFolder = "tests/architecture/ui-text-allowlist";

    private static readonly Lazy<IReadOnlyList<UiFinding>> Findings = new(() =>
        InterfaceTextScanner.ScanRepository().Where(f => InterfaceTextScanner.GuardedRules.Contains(f.Rule)).ToList());

    /// <summary>The fourteen services, the gateway and the shared library: every project that holds views.</summary>
    public static readonly IReadOnlyList<string> Projects =
    [
        "Admin.Logistics.Web", "Auth.Directory.Web", "Compliance.Web", "Dev.Analytics.Web", "File.Storage.Web", "HR.Contracts.Web",
        "HR.Core.Web", "HR.Health.Web", "HR.Movements.Web", "HR.Training.Web", "HRManagement.Gateway", "HRManagement.Web.Shared",
        "IDCard.Web", "IT.Infra.Web", "Payroll.Web", "Records.Archive.Web",
    ];

    public static TheoryData<string> ProjectData()
    {
        var data = new TheoryData<string>();
        foreach (var p in Projects)
            data.Add(p);
        return data;
    }

    private static CeilingList ListOf(string project) => CeilingList.Load($"{ListFolder}/{project}.txt");

    [Fact]
    public void The_scan_reads_every_view_of_the_repository()
    {
        var views = InterfaceTextScanner.ViewFiles();
        Assert.True(views.Count >= 600, $"Only {views.Count} views found under src/: the scan lost its root.");
        var projects = views.Select(InterfaceTextScanner.ProjectOf).Distinct().ToHashSet(StringComparer.Ordinal);
        var unknown = projects.Where(p => !Projects.Contains(p)).ToList();
        Assert.True(unknown.Count == 0, "Views in projects the guard does not list: " + string.Join(", ", unknown));
    }

    [Theory]
    [MemberData(nameof(ProjectData))]
    public void Every_finding_is_a_row_of_its_list(string project)
    {
        var list = ListOf(project);
        var offenders = Findings.Value.Where(f => f.Project == project).ToList();
        var unlisted = CeilingChecks.Unlisted(offenders.Select(f => f.Key), list);
        Assert.True(unlisted.Count == 0,
            $"{project}: {unlisted.Count} interface finding(s) not in {list.RelativePath} (fix the view; the list only shrinks):\n" +
            string.Join("\n", offenders.Where(f => unlisted.Contains(f.Key)).Select(f => $"  {f.File}:{f.Line} [{f.Rule}] {f.Snippet}")));
    }

    [Theory]
    [MemberData(nameof(ProjectData))]
    public void No_list_row_outlives_its_finding(string project)
    {
        var list = ListOf(project);
        var stale = CeilingChecks.Stale(Findings.Value.Where(f => f.Project == project).Select(f => f.Key), list);
        Assert.True(stale.Count == 0, $"{list.RelativePath}: rows that no longer match a finding (remove them):\n  " + string.Join("\n  ", stale));
    }

    [Theory]
    [MemberData(nameof(ProjectData))]
    public void Every_list_is_well_formed(string project)
    {
        var list = ListOf(project);
        Assert.True(list.Malformed.Count == 0, $"{list.RelativePath}:\n  " + string.Join("\n  ", list.Malformed));
    }

    [Fact]
    public void No_list_exists_for_a_project_without_views()
    {
        var folder = Path.Combine(RepoPaths.Root, ListFolder);
        if (!Directory.Exists(folder))
            return;
        var strays = Directory.EnumerateFiles(folder, "*.txt")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && !Projects.Contains(n))
            .ToList();
        Assert.True(strays.Count == 0, $"{ListFolder} holds lists for unknown projects: " + string.Join(", ", strays));
    }

    [Fact]
    public void Every_technical_exception_names_a_file_that_exists_and_its_reason()
    {
        var views = InterfaceTextScanner.ViewFiles();
        foreach (var (file, member, reason) in InterfaceTextScanner.TechnicalIds)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{file} {member}: a technical exception needs its reason");
            Assert.True(views.Any(v => v.EndsWith(file, StringComparison.Ordinal)), $"Technical exception for a view that does not exist: {file}");
        }
    }
}
