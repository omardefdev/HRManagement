using System.Globalization;
using System.Text.Json;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-59 (P-8): the payroll months' registry (<c>testdata/acceptance/payroll-months.json</c>). Its absolute months — an entry's
/// <c>from..to</c> and a class's window — overlap nowhere on one stack; PLAN-57's own file is inside AC-LVA-57's entry; every entry names a
/// stack the registry declares, and every class it names exists in the acceptance project.
/// </summary>
public sealed class Pay059PayrollMonthsRegistryTests
{
    private static readonly string RegistryPath = Path.Combine(RepoPaths.Root, "testdata", "acceptance", "payroll-months.json");

    private static DateOnly Month(string text) => DateOnly.ParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IReadOnlyList<DateOnly> Range(string from, string to)
    {
        var months = new List<DateOnly>();
        for (var m = Month(from); m <= Month(to); m = m.AddMonths(1))
            months.Add(m);
        return months;
    }

    internal static (IReadOnlyList<string> Stacks, IReadOnlyList<(string Owner, string? Class, IReadOnlyList<string> Stacks, IReadOnlyList<DateOnly> Months, string? Rule, string? File)> Entries) Load(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var stacks = root.GetProperty("stacks").EnumerateArray().Select(s => s.GetString()!).ToList();
        var entries = new List<(string, string?, IReadOnlyList<string>, IReadOnlyList<DateOnly>, string?, string?)>();
        foreach (var e in root.GetProperty("entries").EnumerateArray())
        {
            IReadOnlyList<DateOnly> months = [];
            if (e.TryGetProperty("from", out var from))
                months = Range(from.GetString()!, e.GetProperty("to").GetString()!);
            else if (e.TryGetProperty("window", out var window))
                months = Range(window.GetProperty("from").GetString()!, window.GetProperty("to").GetString()!);
            entries.Add((
                e.GetProperty("owner").GetString()!,
                e.TryGetProperty("class", out var c) ? c.GetString() : null,
                e.GetProperty("stacks").EnumerateArray().Select(s => s.GetString()!).ToList(),
                months,
                e.TryGetProperty("rule", out var r) ? r.GetString() : null,
                e.TryGetProperty("file", out var f) ? f.GetString() : null));
        }

        return (stacks, entries);
    }

    /// <summary>The months two owners both take on one stack (empty when the registry is sound).</summary>
    internal static IReadOnlyList<string> Overlaps(string json)
    {
        var (_, entries) = Load(json);
        return entries
            .SelectMany(e => e.Stacks.SelectMany(stack => e.Months.Select(month => (stack, month, e.Owner))))
            .GroupBy(x => (x.stack, x.month))
            .Where(g => g.Select(x => x.Owner).Distinct().Count() > 1)
            .Select(g => $"{g.Key.stack} {g.Key.month:yyyy-MM}: {string.Join(", ", g.Select(x => x.Owner).Distinct())}")
            .ToList();
    }

    [Fact]
    public void The_absolute_months_overlap_nowhere_on_one_stack()
    {
        Assert.Empty(Overlaps(System.IO.File.ReadAllText(RegistryPath)));
    }

    [Fact]
    public void An_overlap_planted_in_a_copy_of_the_registry_is_found()
    {
        var planted = System.IO.File.ReadAllText(RegistryPath).Replace(
            "\"window\": { \"from\": \"2024-01\", \"to\": \"2024-06\" }", "\"window\": { \"from\": \"2024-01\", \"to\": \"2024-07\" }", StringComparison.Ordinal);
        Assert.NotEqual(System.IO.File.ReadAllText(RegistryPath), planted);
        Assert.Equal(["prodlike-main 2024-07: AC-21, AC-22"], Overlaps(planted));
    }

    [Fact]
    public void PLAN_57s_file_is_inside_AC_LVA_57s_entry()
    {
        var (_, entries) = Load(System.IO.File.ReadAllText(RegistryPath));
        var lva = Assert.Single(entries, e => e.Owner == "AC-LVA-57");
        Assert.Equal("testdata/acceptance/lva057-periods.json", lva.File);
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "testdata", "acceptance", "lva057-periods.json")));
        var reserved = doc.RootElement.GetProperty("months").EnumerateObject().Select(p => Month(p.Value.GetString()!)).Distinct().ToList();
        Assert.NotEmpty(reserved);
        Assert.All(reserved, month => Assert.Contains(month, lva.Months));
    }

    [Fact]
    public void Every_entry_names_a_declared_stack_and_an_existing_class_and_a_month_or_a_rule()
    {
        var (stacks, entries) = Load(System.IO.File.ReadAllText(RegistryPath));
        var acceptance = Path.Combine(RepoPaths.Root, "src", "Tests", "HRManagement.AcceptanceTests");
        foreach (var e in entries)
        {
            Assert.All(e.Stacks, stack => Assert.Contains(stack, stacks));
            Assert.True(e.Months.Count > 0 || e.Rule is not null, $"{e.Owner} gives neither months nor a rule.");
            if (e.Class is not null)
                Assert.True(System.IO.File.Exists(Path.Combine(acceptance, e.Class + ".cs")), $"{e.Owner}: no {e.Class}.cs in the acceptance project.");
        }

        // The classes this plan places: each once.
        foreach (var owner in new[] { "AC-12", "AC-21", "AC-22", "AC-23", "AC-PAY-59" })
            Assert.Single(entries, e => e.Owner == owner);
    }
}
