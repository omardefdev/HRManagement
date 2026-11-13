using System.Text;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-67 (0.4): the generated inventory of the interface. With <c>UX067_INVENTORY_OUT</c> set to a folder it writes
/// <c>ui-inventory.tsv</c> (one row per finding) and <c>ui-inventory.md</c> (counts per project and kind); without it the test does
/// nothing. The counts come from <see cref="InterfaceTextScanner"/>, the guard's own scanner, so the inventory and the guard agree.
/// </summary>
public sealed class InterfaceTextInventoryReport
{
    [Fact]
    public void Writes_the_interface_inventory_when_asked()
    {
        var outDir = Environment.GetEnvironmentVariable("UX067_INVENTORY_OUT");
        if (string.IsNullOrWhiteSpace(outDir))
            return;

        Directory.CreateDirectory(outDir);
        var views = InterfaceTextScanner.ViewFiles();
        var findings = InterfaceTextScanner.ScanRepository();

        var tsv = new StringBuilder("project\tfile\tline\trule\tsnippet\tkey\n");
        foreach (var f in findings)
            tsv.Append($"{f.Project}\t{f.File}\t{f.Line}\t{f.Rule}\t{f.Snippet.Replace('\t', ' ')}\t{f.Key.Replace('\t', ' ')}\n");
        global::System.IO.File.WriteAllText(Path.Combine(outDir, "ui-inventory.tsv"), tsv.ToString(), new UTF8Encoding(false));

        var kinds = InterfaceTextScanner.GuardedRules
            .Concat([InterfaceTextScanner.InlineScript, InterfaceTextScanner.InlineStyle, InterfaceTextScanner.DateTimeLocal])
            .ToList();
        var projects = views.Select(InterfaceTextScanner.ProjectOf).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        var md = new StringBuilder();
        md.AppendLine($"# Interface inventory ({DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ})");
        md.AppendLine();
        md.AppendLine($"Views scanned: {views.Count}. Findings: {findings.Count}.");
        md.AppendLine();
        md.AppendLine("| Project | Views | " + string.Join(" | ", kinds) + " |");
        md.AppendLine("|---|---|" + string.Concat(kinds.Select(_ => "---|")));
        foreach (var p in projects)
        {
            var count = views.Count(v => InterfaceTextScanner.ProjectOf(v) == p);
            md.AppendLine($"| {p} | {count} | " + string.Join(" | ", kinds.Select(k => findings.Count(f => f.Project == p && f.Rule == k))) + " |");
        }

        md.AppendLine($"| **Total** | {views.Count} | " + string.Join(" | ", kinds.Select(k => findings.Count(f => f.Rule == k))) + " |");
        md.AppendLine();
        md.AppendLine("Files with at least one finding, per kind:");
        md.AppendLine();
        foreach (var k in kinds)
            md.AppendLine($"- {k}: {findings.Where(f => f.Rule == k).Select(f => f.File).Distinct().Count()} files");
        global::System.IO.File.WriteAllText(Path.Combine(outDir, "ui-inventory.md"), md.ToString(), new UTF8Encoding(false));
    }
}
