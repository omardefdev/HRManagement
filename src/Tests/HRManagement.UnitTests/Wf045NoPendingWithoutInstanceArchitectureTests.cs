using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-45 (rule 1 of section 1): only <c>AssetApprovalRules.LinkPending</c> writes <c>PendingApproval</c>, and it writes the instance with
/// it, so no path in Admin.Logistics can save a pending request that has no instance.
/// </summary>
public sealed partial class Wf045NoPendingWithoutInstanceArchitectureTests
{
    private const string RulesFile = "AssetApprovalRules.cs";

    private static readonly string[] InstanceWriters = [RulesFile, "AssetApprovalLoop.cs"];

    [GeneratedRegex(@"(?<![=!<>])=\s*(?:\w+\s*\?\s*)?Asset(?:Assignment|LossReport)Status\.PendingApproval\b")]
    private static partial Regex PendingWrite();

    [GeneratedRegex(@"\bWorkflowInstanceId\s*=(?!=)")]
    private static partial Regex InstanceWrite();

    private static string ServiceRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "src", "Services", "AdminLogistics");
        }
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(ServiceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    [Fact]
    public void The_scan_reads_the_files_that_matter()
    {
        var names = Sources().Select(Path.GetFileName).ToList();

        Assert.Contains("AssetsController.cs", names);
        Assert.Contains(RulesFile, names);
        Assert.Contains("AssetLossReport.cs", names);
    }

    [Fact]
    public void Only_the_rules_write_pending()
    {
        var offenders = Sources()
            .Where(f => Path.GetFileName(f) != RulesFile && PendingWrite().IsMatch(System.IO.File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, $"PendingApproval written outside {RulesFile}: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Only_the_rules_and_the_loop_write_the_instance()
    {
        var offenders = Sources()
            .Where(f => !InstanceWriters.Contains(Path.GetFileName(f)) && InstanceWrite().IsMatch(StripComparisons(System.IO.File.ReadAllText(f))))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, $"WorkflowInstanceId written outside the loop: {string.Join(", ", offenders)}");
    }

    [Theory]
    [InlineData("assignment.Status = AssetAssignmentStatus.PendingApproval;")]
    [InlineData("Status = requiresApproval ? AssetAssignmentStatus.PendingApproval : AssetAssignmentStatus.Active")]
    [InlineData("public AssetLossReportStatus Status { get; set; } = AssetLossReportStatus.PendingApproval;")]
    public void The_guard_sees_a_write(string source) =>
        Assert.Matches(PendingWrite(), source);

    [Theory]
    [InlineData("if (x.Status == AssetAssignmentStatus.PendingApproval)")]
    [InlineData("if (x.Status != AssetLossReportStatus.PendingApproval)")]
    public void The_guard_leaves_a_comparison_alone(string source) =>
        Assert.DoesNotMatch(PendingWrite(), source);

    /// <summary>A public property of an entity declares the column; it writes nothing.</summary>
    private static string StripComparisons(string source) =>
        source.Replace("public Guid? WorkflowInstanceId { get; set; }", string.Empty, StringComparison.Ordinal);
}
