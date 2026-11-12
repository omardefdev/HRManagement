using HRManagement.TestSupport;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-51 (§6, P-8): a ceiling list under <c>tests/architecture/</c>. One row per line: <c>Key | reason</c>; <c>#</c> starts a comment. A
/// missing file is an empty list. A row's reason starts with the plan that owns the row (<c>PLAN-53:</c>, <c>IAM-F:</c>): no row exists
/// to save time, and PLAN-65 reviews them.
/// </summary>
public sealed class CeilingList
{
    /// <summary>The plans that may own a row: PLAN-53 (Payroll's writes) and wave 2 IAM-F (the entity bindings outside HR.Movements). PLAN-51
    /// owned the rows its P1 seeded until its P4 emptied them.</summary>
    public static readonly IReadOnlyList<string> Owners = ["PLAN-53", "IAM-F"];

    private CeilingList(string relativePath, IReadOnlyDictionary<string, string> rows, IReadOnlyList<string> malformed)
    {
        RelativePath = relativePath;
        Rows = rows;
        Malformed = malformed;
    }

    public string RelativePath { get; }

    public IReadOnlyDictionary<string, string> Rows { get; }

    /// <summary>Lines without a reason, with a reason no owner signs, or repeated keys.</summary>
    public IReadOnlyList<string> Malformed { get; }

    public static CeilingList Load(string relativePath) =>
        Parse(relativePath, global::System.IO.File.Exists(Path.Combine(RepoPaths.Root, relativePath))
            ? global::System.IO.File.ReadAllLines(Path.Combine(RepoPaths.Root, relativePath))
            : []);

    public static CeilingList Parse(string relativePath, IEnumerable<string> lines)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var malformed = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                malformed.Add($"{line} (no reason)");
                continue;
            }

            if (!Owners.Any(o => parts[1].StartsWith(o + ":", StringComparison.Ordinal)))
            {
                malformed.Add($"{line} (the reason does not start with an owning plan: {string.Join(", ", Owners)})");
                continue;
            }

            if (!rows.TryAdd(parts[0], parts[1]))
                malformed.Add($"{line} (repeated)");
        }

        return new CeilingList(relativePath, rows, malformed);
    }
}

/// <summary>The common checks of a ceiling list against what a scan finds.</summary>
public static class CeilingChecks
{
    /// <summary>Offenders the list does not name.</summary>
    public static IReadOnlyList<string> Unlisted(IEnumerable<string> offenders, CeilingList list) =>
        offenders.Distinct(StringComparer.Ordinal).Where(o => !list.Rows.ContainsKey(o)).OrderBy(o => o, StringComparer.Ordinal).ToList();

    /// <summary>Rows that no longer offend: the list only shrinks, so they must go.</summary>
    public static IReadOnlyList<string> Stale(IEnumerable<string> offenders, CeilingList list)
    {
        var set = offenders.ToHashSet(StringComparer.Ordinal);
        return list.Rows.Keys.Where(k => !set.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }
}
