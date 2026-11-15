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
        md.AppendLine();
        md.AppendLine($"Views whose @model the asp-for label check could not resolve: {InterfaceTextScanner.UnresolvedModels.Distinct().Count()} (ui-unresolved-models.txt).");
        global::System.IO.File.WriteAllText(Path.Combine(outDir, "ui-inventory.md"), md.ToString(), new UTF8Encoding(false));
        global::System.IO.File.WriteAllText(Path.Combine(outDir, "ui-unresolved-models.txt"),
            string.Join("\n", InterfaceTextScanner.UnresolvedModels.Distinct().Order()) + "\n", new UTF8Encoding(false));

        if (string.Equals(Environment.GetEnvironmentVariable("UX067_SEED_LISTS"), "true", StringComparison.OrdinalIgnoreCase))
            SeedLists(findings);
        if (string.Equals(Environment.GetEnvironmentVariable("UX067_PRUNE_LISTS"), "true", StringComparison.OrdinalIgnoreCase))
            PruneLists(findings, outDir);
    }

    /// <summary>
    /// P3–P6: removes the rows whose finding is gone (the lists only shrink) and writes, into <c>ui-unlisted.txt</c>, any finding that has
    /// no row: those are fixed in the view, never added to a list.
    /// </summary>
    private static void PruneLists(IReadOnlyList<UiFinding> findings, string outDir)
    {
        var keys = findings.Where(f => InterfaceTextScanner.GuardedRules.Contains(f.Rule)).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var unlisted = new List<string>();
        foreach (var project in InterfaceTextGuardTests.Projects)
        {
            var relative = $"{InterfaceTextGuardTests.ListFolder}/{project}.txt";
            var path = Path.Combine(HRManagement.TestSupport.RepoPaths.Root, relative);
            var list = CeilingList.Load(relative);
            unlisted.AddRange(findings.Where(f => f.Project == project && InterfaceTextScanner.GuardedRules.Contains(f.Rule) && !list.Rows.ContainsKey(f.Key))
                .Select(f => $"{f.File}:{f.Line} [{f.Rule}] {f.Snippet}"));
            if (!global::System.IO.File.Exists(path))
                continue;
            var lines = global::System.IO.File.ReadAllLines(path);
            var kept = lines.Where(l => l.TrimStart().StartsWith('#') || l.Trim().Length == 0 || keys.Contains(l.Split('|', 2)[0].Trim())).ToList();
            if (kept.All(l => l.TrimStart().StartsWith('#') || l.Trim().Length == 0))
                global::System.IO.File.Delete(path);
            else
                global::System.IO.File.WriteAllText(path, string.Join("\n", kept) + "\n", new UTF8Encoding(false));
        }

        global::System.IO.File.WriteAllText(Path.Combine(outDir, "ui-unlisted.txt"), string.Join("\n", unlisted) + "\n", new UTF8Encoding(false));
    }

    /// <summary>The phase of PLAN-67 that empties each kind of row (P2 seeds the lists once; the guard then only lets them shrink).</summary>
    private static readonly IReadOnlyDictionary<string, string> RowReasons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [InterfaceTextScanner.EnglishLabel] = "PLAN-67: P3 gives the label its Arabic text",
        [InterfaceTextScanner.RawStatus] = "PLAN-67: P3 prints the value by its Arabic name",
        [InterfaceTextScanner.UnformattedTime] = "PLAN-67: P3 formats it with HrDate/HrDateTime in the configured zone",
        [InterfaceTextScanner.PrintedId] = "PLAN-67: P4 shows the name instead of the id",
        [InterfaceTextScanner.EmptyTable] = "PLAN-67: P4 adds the empty message",
        [InterfaceTextScanner.RawJson] = "PLAN-67: P5 moves the JSON behind the technical view",
        [InterfaceTextScanner.InlineHandler] = "PLAN-67: P1 moves it to data-hr-confirm or a static file",
        [InterfaceTextScanner.StyleAttribute] = "PLAN-67: P1 makes it a class",
    };

    /// <summary>P2 (P-12): writes one ceiling list per project from the guard's findings, each row signed by PLAN-67.</summary>
    private static void SeedLists(IReadOnlyList<UiFinding> findings)
    {
        var folder = Path.Combine(HRManagement.TestSupport.RepoPaths.Root, InterfaceTextGuardTests.ListFolder);
        Directory.CreateDirectory(folder);
        foreach (var project in InterfaceTextGuardTests.Projects)
        {
            var rows = findings
                .Where(f => f.Project == project && InterfaceTextScanner.GuardedRules.Contains(f.Rule))
                .OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => $"{f.Key} | {RowReasons[f.Rule]}")
                .ToList();
            var path = Path.Combine(folder, $"{project}.txt");
            if (rows.Count == 0)
            {
                if (global::System.IO.File.Exists(path))
                    global::System.IO.File.Delete(path);
                continue;
            }

            var header = $"# PLAN-67 (P-12): what InterfaceTextGuardTests found in {project} when P2 seeded this list. Each row is emptied by the phase its reason names; the list only shrinks.\n";
            global::System.IO.File.WriteAllText(path, header + string.Join("\n", rows) + "\n", new UTF8Encoding(false));
        }
    }
}
