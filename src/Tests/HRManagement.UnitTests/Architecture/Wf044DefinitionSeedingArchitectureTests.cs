using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-44 (G-WF-19): definitions reach the database through <c>WorkflowPackSeeder</c>, or through the governed authoring of PLAN-43. Two seeders
/// that nothing called sat beside it for years, and a third was live: a definition seeded elsewhere skips the pack's rules.
/// </summary>
public sealed partial class Wf044DefinitionSeedingArchitectureTests
{
    private static readonly string[] Allowed =
    [
        "src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs",
        "src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowDefinitionLifecycleService.cs"
    ];

    [GeneratedRegex(@"new\s+WorkflowDefinition\b(?!Snapshot)")]
    private static partial Regex Creates();

    internal static bool SeedsADefinition(string source) => Creates().IsMatch(source);

    [Fact]
    public void Only_the_pack_seeder_and_the_governed_authoring_create_a_definition()
    {
        // Resolve finds files only, so the folder comes from the solution beside it.
        var root = Path.Combine(Path.GetDirectoryName(RepoPaths.Resolve("HRManagement.slnx"))!, "src", "Services");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f))
            .Select(f => (Path: Relative(f), Text: global::System.IO.File.ReadAllText(f)))
            .Where(f => SeedsADefinition(f.Text) && !Allowed.Contains(f.Path, StringComparer.OrdinalIgnoreCase))
            .Select(f => f.Path)
            .ToList();

        Assert.True(offenders.Count == 0, "A definition is seeded outside WorkflowPackSeeder: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Each_allowed_file_still_creates_one_so_the_list_cannot_go_stale()
    {
        Assert.All(Allowed, path => Assert.True(SeedsADefinition(global::System.IO.File.ReadAllText(RepoPaths.Resolve(path))), path));
    }

    [Theory]
    [InlineData("db.WorkflowDefinitions.Add(new WorkflowDefinition { Code = \"X\" });", true)]
    [InlineData("var d = new WorkflowDefinition\n{\n};", true)]
    [InlineData("var s = new WorkflowDefinitionSnapshot();", false)]
    [InlineData("WorkflowDefinition? found = null;", false)]
    public void The_guard_reads_a_creation_and_nothing_else(string source, bool expected)
    {
        Assert.Equal(expected, SeedsADefinition(source));
    }

    private static bool IsGenerated(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/obj/") || p.Contains("/bin/") || p.Contains("/Migrations/");
    }

    private static string Relative(string path)
    {
        var p = path.Replace('\\', '/');
        var at = p.IndexOf("src/Services/", StringComparison.Ordinal);
        return at >= 0 ? p[at..] : p;
    }
}
