using System.Text.RegularExpressions;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-33 (AC-HCM-33a): an acceptance test may not pass without checking. Textual scan of
/// <c>src/Tests/HRManagement.AcceptanceTests/*.cs</c>: no <c>WriteResult("Skip"…)</c>, no Pass/Fail/Blocked result
/// followed by <c>return;</c>, Blocked only through <c>AcceptanceGate</c>, and every test method of an <c>Ac*</c>
/// class asserts something.
/// </summary>
public class AcceptanceSkipPatternArchitectureTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !global::System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
        }
    }

    private static string AcceptanceDir => Path.Combine(RepoRoot, "src", "Tests", "HRManagement.AcceptanceTests");

    /// <summary>
    /// Helpers that assert on behalf of the test method (each throws on a failed check). A test method whose body
    /// calls one of these counts as asserting.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> AssertingHelpers = new Dictionary<string, string>
    {
        ["Assert."] = "xUnit assertion",
        ["AcceptanceGate.RequireData("] = "fails or skips — never passes — when the precondition is false",
        ["E2eHttp.EnsureNotServerError("] = "throws on a 5xx response",
        [".EnsureSuccessStatusCode("] = "throws on a non-success response",
        ["Should()"] = "assertion library",
    };

    /// <summary>Test methods that legitimately contain no assertion of their own, with the reason.</summary>
    internal static readonly IReadOnlyDictionary<string, string> NoAssertAllowList = new Dictionary<string, string>();

    private static readonly Regex TestAttribute = new(
        @"\[(?:Fact|SkippableFact|Theory|SkippableTheory)\b[^\]]*\]", RegexOptions.Compiled);

    private static readonly Regex MethodName = new(
        @"\b(?:public|internal|private|protected)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\],\s?]+?\s+(\w+)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex SkipResult = new(@"WriteResult\s*\([^;]*""Skip""", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex BlockedResult = new(@"WriteResult\s*\(\s*""Blocked""", RegexOptions.Compiled);
    private static readonly Regex TerminalResult = new(@"WriteResult\s*\(\s*""(Pass|Fail|Blocked)""", RegexOptions.Compiled);
    private static readonly Regex RawSkip = new(@"\bSkip\.If(?:Not)?\s*\(", RegexOptions.Compiled);

    [Fact]
    public void Acceptance_tests_never_record_skip_results()
    {
        var offenders = Scan(f => FindSkipResults(f.Text).Select(l => $"{f.Name}:{l}"));
        Assert.True(offenders.Count == 0,
            "Use AcceptanceGate.RequireData (Blocked + Skip) instead of WriteResult(\"Skip\"):\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Acceptance_tests_never_return_after_a_result()
    {
        var offenders = Scan(f => FindResultThenReturn(f.Text).Select(l => $"{f.Name}:{l}"));
        Assert.True(offenders.Count == 0,
            "A Pass/Fail/Blocked result followed by return lets the test pass without checking:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Only_the_gate_records_blocked()
    {
        var offenders = Scan(f => f.Name == "AcceptanceGate.cs"
            ? []
            : FindBlockedResults(f.Text).Select(l => $"{f.Name}:{l}"));
        Assert.True(offenders.Count == 0,
            "Record Blocked through AcceptanceGate.RequireData so the test is skipped or failed:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Acceptance_tests_skip_only_through_the_gate()
    {
        var offenders = Scan(f => f.Name == "AcceptanceGate.cs"
            ? []
            : FindRawSkips(f.Text).Select(l => $"{f.Name}:{l}"));
        Assert.True(offenders.Count == 0,
            "A raw Skip.If writes no Blocked evidence and ignores ACCEPTANCE_REQUIRE_ALL; use AcceptanceGate.RequireData:\n" +
            string.Join('\n', offenders));
    }

    [Fact]
    public void Every_acceptance_scenario_method_asserts()
    {
        var offenders = Scan(f => f.Name.StartsWith("Ac", StringComparison.Ordinal) && !f.Name.StartsWith("Acceptance", StringComparison.Ordinal)
            ? FindTestMethodsWithoutAssertion(f.Text)
                .Where(m => !NoAssertAllowList.ContainsKey($"{Path.GetFileNameWithoutExtension(f.Name)}.{m}"))
                .Select(m => $"{f.Name}: {m}")
            : []);
        Assert.True(offenders.Count == 0,
            "Acceptance test methods must contain an assertion (Assert.* or a documented asserting helper):\n" +
            string.Join('\n', offenders));
    }

    [Fact]
    public void Scanner_flags_each_forbidden_pattern()
    {
        const string skip = """
            recorder.WriteResult("Skip", "no data");
            return;
            """;
        const string ternarySkip = """
            recorder.WriteResult(found
                ? "Pass"
                : "Skip");
            """;
        const string passReturn = """
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                recorder.WriteResult("Pass", "not seeded");
                return;
            }
            """;
        const string blockedReturn = """
            recorder.WriteResult("Blocked", "no data");

            return;
            """;
        const string noAssert = """
            public class AcSample
            {
                [SkippableFact]
                public async Task Looks_but_never_checks()
                {
                    AcceptanceEnv.RequireEnabled();
                    var body = await http.GetStringAsync("x");
                    recorder.WriteResult("Pass");
                }

                [Fact]
                public void Checks() => Assert.True(true);

                [SkippableFact]
                public async Task Gated()
                {
                    AcceptanceGate.RequireData(recorder, ok, "ar", "en");
                    recorder.WriteResult("Pass");
                }
            }
            """;

        Assert.Single(FindSkipResults(skip));
        Assert.Single(FindSkipResults(ternarySkip));
        Assert.Single(FindResultThenReturn(passReturn));
        Assert.Single(FindResultThenReturn(blockedReturn));
        Assert.Single(FindBlockedResults(blockedReturn));
        Assert.Single(FindRawSkips("Skip.If(positionId is null, \"none left\");"));
        Assert.Single(FindRawSkips("Skip.IfNot(ok, \"x\");"));
        Assert.Equal(["Looks_but_never_checks"], FindTestMethodsWithoutAssertion(noAssert));
    }

    [Fact]
    public void Scanner_accepts_the_gate_pattern()
    {
        const string gated = """
            AcceptanceGate.RequireData(recorder, resp.StatusCode != HttpStatusCode.NotFound, "ar", "en");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            recorder.WriteResult("Pass");
            }
            """;
        Assert.Empty(FindSkipResults(gated));
        Assert.Empty(FindResultThenReturn(gated));
        Assert.Empty(FindBlockedResults(gated));
        Assert.Empty(FindRawSkips(gated));
    }

    private sealed record SourceFile(string Name, string Text);

    private static List<string> Scan(Func<SourceFile, IEnumerable<string>> check)
    {
        Assert.True(Directory.Exists(AcceptanceDir), $"Missing {AcceptanceDir}");
        return Directory.EnumerateFiles(AcceptanceDir, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(f => check(new SourceFile(Path.GetFileName(f), global::System.IO.File.ReadAllText(f).Replace("\r\n", "\n"))))
            .ToList();
    }

    internal static IReadOnlyList<int> FindSkipResults(string text) =>
        SkipResult.Matches(Normalize(text)).Select(m => LineOf(text, m.Index)).ToList();

    internal static IReadOnlyList<int> FindRawSkips(string text) =>
        RawSkip.Matches(Normalize(text)).Select(m => LineOf(text, m.Index)).ToList();

    internal static IReadOnlyList<int> FindBlockedResults(string text) =>
        BlockedResult.Matches(Normalize(text)).Select(m => LineOf(text, m.Index)).ToList();

    internal static IReadOnlyList<int> FindResultThenReturn(string text)
    {
        var lines = Normalize(text).Split('\n');
        var hits = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!TerminalResult.IsMatch(lines[i]))
                continue;
            for (var j = i + 1; j <= Math.Min(i + 3, lines.Length - 1); j++)
            {
                if (Regex.IsMatch(lines[j], @"^\s*return\s*;"))
                {
                    hits.Add(i + 1);
                    break;
                }
            }
        }
        return hits;
    }

    internal static IReadOnlyList<string> FindTestMethodsWithoutAssertion(string text)
    {
        text = Normalize(text);
        var result = new List<string>();
        foreach (Match attr in TestAttribute.Matches(text))
        {
            var sig = MethodName.Match(text, attr.Index + attr.Length);
            if (!sig.Success)
                continue;
            var name = sig.Groups[1].Value;
            var body = MethodBody(text, sig.Index + sig.Length);
            if (!AssertingHelpers.Keys.Any(h => body.Contains(h, StringComparison.Ordinal)))
                result.Add(name);
        }
        return result;
    }

    /// <summary>The method body from the end of its signature: a braced block or an expression body up to ';'.</summary>
    private static string MethodBody(string text, int from)
    {
        var parens = 1;
        var i = from;
        while (i < text.Length && parens > 0)
        {
            if (text[i] == '(') parens++;
            else if (text[i] == ')') parens--;
            i++;
        }

        var brace = text.IndexOf('{', i);
        var arrow = text.IndexOf("=>", i, StringComparison.Ordinal);
        if (arrow >= 0 && (brace < 0 || arrow < brace))
        {
            var end = text.IndexOf(";\n", arrow, StringComparison.Ordinal);
            return end < 0 ? text[arrow..] : text[arrow..end];
        }
        if (brace < 0)
            return string.Empty;

        var depth = 0;
        for (var k = brace; k < text.Length; k++)
        {
            if (text[k] == '{') depth++;
            else if (text[k] == '}' && --depth == 0)
                return text[brace..(k + 1)];
        }
        return text[brace..];
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static int LineOf(string text, int index) =>
        Normalize(text)[..index].Count(c => c == '\n') + 1;
}
