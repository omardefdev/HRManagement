using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// QA-034: every date in the repository that can stop CI or a service on a calendar day is registered in
/// <c>docs/qa/dated-obligations.json</c>, and <c>scripts/qa/dated-obligations-check.ps1</c> enforces the horizon.
/// </summary>
public class DatedObligationsTests
{
    private const string RegistryPath = "docs/qa/dated-obligations.json";
    private const string FixtureAsOf = "2026-11-01";

    private static readonly string[] SkippedDirectories =
        // "out": testdata/<card>/out files are rewritten by the validation scripts on every run, never authored.
        ["bin", "obj", "node_modules", ".git", "artifacts", "TestResults", ".vs", ".idea", "coveragereport", "out"];

    private static JsonElement Registry
    {
        get
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, RegistryPath)));
            return doc.RootElement.Clone();
        }
    }

    private static string[] Keys => Registry.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).ToArray();

    private static IEnumerable<string> RepoJsonFiles()
    {
        var root = RepoPaths.Root;
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                // Dot directories (.git, .claude/worktrees with whole repository copies, .vs) are not repository content.
                if (!name.StartsWith('.') && !SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                    pending.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                yield return Path.GetRelativePath(root, file).Replace('\\', '/');
        }
    }

    /// <summary>Normalised JSON paths ("exceptions[*].expiresOn") of every property whose name is a registry key.</summary>
    private static HashSet<string> DatedPaths(string relativeFile, string[] keys)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, relativeFile)),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return result;
        }

        using (doc)
            Walk(doc.RootElement, "");
        return result;

        void Walk(JsonElement el, string path)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject())
                    {
                        var child = path.Length == 0 ? p.Name : $"{path}.{p.Name}";
                        if (keys.Contains(p.Name, StringComparer.Ordinal))
                            result.Add(child);
                        Walk(p.Value, child);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray())
                        Walk(item, path + "[*]");
                    break;
            }
        }
    }

    private static bool MatchesHistorical(string file, string pattern) =>
        pattern.EndsWith("/**", StringComparison.Ordinal)
            ? file.StartsWith(pattern[..^2], StringComparison.Ordinal)
            : string.Equals(file, pattern, StringComparison.Ordinal);

    private static string LeafKey(string dottedPath) => dottedPath.Split('.')[^1];

    [Fact]
    public void Every_file_with_a_dated_key_is_registered_or_historical()
    {
        var registry = Registry;
        var keys = Keys;
        var keyRegex = new Regex("\"(" + string.Join("|", keys.Select(Regex.Escape)) + ")\"\\s*:", RegexOptions.Compiled);

        var obligations = registry.GetProperty("obligations").EnumerateArray().ToList();
        var historical = registry.GetProperty("historical").EnumerateArray().ToList();

        var problems = new List<string>();
        foreach (var file in RepoJsonFiles())
        {
            if (!keyRegex.IsMatch(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, file))))
                continue;
            if (file == RegistryPath)
                continue;

            var dated = DatedPaths(file, keys);
            if (dated.Count == 0)
                continue;

            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ob in obligations.Where(o => o.GetProperty("file").GetString() == file))
            {
                var selector = ob.GetProperty("selector").GetString()!;
                var dateField = ob.GetProperty("dateField").GetString()!;
                covered.Add(selector == "$" ? dateField : $"{selector}.{dateField}");
            }

            var historicalKeys = historical
                .Where(h => MatchesHistorical(file, h.GetProperty("file").GetString()!))
                .SelectMany(h => h.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var path in dated.Where(p => !covered.Contains(p) && !historicalKeys.Contains(LeafKey(p))))
                problems.Add($"{file}: '{path}' is a dated key but no obligation in {RegistryPath} covers it and it is not historical");
        }

        Assert.True(problems.Count == 0,
            "Unregistered dates (register them in docs/qa/dated-obligations.json, or list them under historical with a reason):\n"
            + string.Join("\n", problems));
    }

    [Fact]
    public void Registry_entries_are_well_formed_and_point_at_existing_files()
    {
        var registry = Registry;
        var keys = Keys;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ob in registry.GetProperty("obligations").EnumerateArray())
        {
            var id = ob.GetProperty("id").GetString()!;
            Assert.True(ids.Add(id), $"duplicate obligation id {id}");
            var file = ob.GetProperty("file").GetString()!;
            Assert.True(System.IO.File.Exists(Path.Combine(RepoPaths.Root, file)), $"{id}: {file} does not exist");
            Assert.Contains(LeafKey(ob.GetProperty("dateField").GetString()!), keys);
            Assert.False(string.IsNullOrWhiteSpace(ob.GetProperty("owner").GetString()), $"{id}: owner required");
            Assert.True(ob.GetProperty("enforcedBy").GetArrayLength() > 0, $"{id}: enforcedBy required");
        }

        foreach (var h in registry.GetProperty("historical").EnumerateArray())
            Assert.False(string.IsNullOrWhiteSpace(h.GetProperty("reason").GetString()), "historical entries need a reason");
    }

    // AC-QA-34b: 31 days -> pass; 30 / 20 days -> warning, exit 0; 7 / 5 days -> fail; past due -> fail;
    // Closed -> ignored; 2026-13-01 -> fail; a selector matching nothing -> fail (stale registration).
    [Theory]
    [InlineData("far-31", 0, false)]
    [InlineData("warn-30", 0, true)]
    [InlineData("warn-20", 0, true)]
    [InlineData("fail-7", 1, false)]
    [InlineData("fail-5", 1, false)]
    [InlineData("expired", 1, false)]
    [InlineData("closed", 0, false)]
    [InlineData("bad-date", 1, false)]
    [InlineData("root-selector", 0, false)]
    [InlineData("stale", 1, false)]
    public void Check_script_applies_the_horizon(string fixture, int expectedExit, bool expectWarning)
    {
        var outFile = Path.Combine(Path.GetTempPath(), $"qa034-do-{fixture}-{Guid.NewGuid():N}.json");
        try
        {
            var (exit, stdout, stderr) = RepoScriptRunner.RunScript(
                Path.Combine(RepoPaths.Root, "scripts", "qa", "dated-obligations-check.ps1"),
                $"-AsOfDate {FixtureAsOf} -Registry testdata/qa/dated-obligations/{fixture}.json -OutFile \"{outFile}\"");

            Assert.True(exit == expectedExit, $"{fixture}: expected exit {expectedExit}, got {exit}\n{stdout}\n{stderr}");
            Assert.Equal(expectWarning, stdout.Contains("::warning::", StringComparison.Ordinal));
            Assert.Equal(expectedExit == 1, stdout.Contains("::error::", StringComparison.Ordinal));

            using var report = JsonDocument.Parse(System.IO.File.ReadAllText(outFile));
            Assert.Equal(FixtureAsOf, report.RootElement.GetProperty("asOfDate").GetString());
            Assert.Equal(expectedExit == 0, report.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            if (System.IO.File.Exists(outFile))
                System.IO.File.Delete(outFile);
        }
    }

    [Fact]
    public void Real_registry_resolves_every_obligation_on_a_fixed_day()
    {
        // The CI step runs the real registry on today's date; here a fixed day only proves every registration resolves
        // (no stale selector, no unparseable date) without reading the clock.
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(
            Path.Combine(RepoPaths.Root, "scripts", "qa", "dated-obligations-check.ps1"),
            $"-AsOfDate 2026-10-30 -OutFile \"{Path.Combine(Path.GetTempPath(), $"qa034-do-real-{Guid.NewGuid():N}.json")}\"");
        Assert.DoesNotContain("stale registration", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("unparseable", stdout, StringComparison.Ordinal);
        Assert.True(exit == 0, $"real registry on 2026-10-30: {stdout}\n{stderr}");
        Assert.Contains("Nearest open obligation:", stdout, StringComparison.Ordinal);
    }
}
