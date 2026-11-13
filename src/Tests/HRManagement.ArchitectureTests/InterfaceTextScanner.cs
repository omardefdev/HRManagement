using System.Text;
using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.ArchitectureTests;

/// <summary>One thing a view shows that PLAN-67 (G-UX-01..10) does not allow, or (inventory kinds) only counts.</summary>
public sealed record UiFinding(string Project, string File, int Line, string Rule, string Snippet)
{
    /// <summary>The ceiling-list key: file, rule and the normalized snippet; a repeated snippet in one file gets <c>#2</c>, <c>#3</c>….</summary>
    public string Key { get; init; } = "";
}

/// <summary>
/// PLAN-67 (§9, P-12): reads every Razor view of the repository as text and finds what the interface must not show or carry:
/// <list type="number">
/// <item><c>english-label</c>: an English word in a <c>th</c>, <c>dt</c>, <c>label</c>, <c>button</c>, <c>legend</c>, <c>option</c> or heading,
/// outside <see cref="Abbreviations"/>;</item>
/// <item><c>raw-status</c>: a status or type property printed as it is in the code (<c>@x.Status</c>);</item>
/// <item><c>printed-id</c>: a printed <c>…Id</c>, <c>…By</c> or <c>UserId</c>, outside <see cref="TechnicalIds"/>;</item>
/// <item><c>raw-json</c>: a printed <c>…Json</c> outside the <c>_TechnicalJson</c> partial;</item>
/// <item><c>inline-handler</c>: an <c>on…=</c> attribute;</item>
/// <item><c>style-attribute</c>: a <c>style=</c> attribute;</item>
/// <item><c>empty-table</c>: a table filled by a loop with no empty message near it;</item>
/// <item><c>unformatted-time</c>: a date or time formatted or printed outside <c>HrDate</c>/<c>HrDateTime</c> and their input helpers.</item>
/// </list>
/// The inventory kinds <c>inline-script</c>, <c>inline-style</c> and <c>datetime-local</c> are counted, not guarded: a block that carries the
/// page's nonce is allowed once a page carries one policy (P-4). The rules are text heuristics over Razor; <c>GuardSelfTests</c> plants a
/// defect for each one.
/// </summary>
public static partial class InterfaceTextScanner
{
    public const string EnglishLabel = "english-label";
    public const string RawStatus = "raw-status";
    public const string PrintedId = "printed-id";
    public const string RawJson = "raw-json";
    public const string InlineHandler = "inline-handler";
    public const string StyleAttribute = "style-attribute";
    public const string EmptyTable = "empty-table";
    public const string UnformattedTime = "unformatted-time";
    public const string InlineScript = "inline-script";
    public const string InlineStyle = "inline-style";
    public const string DateTimeLocal = "datetime-local";

    /// <summary>The eight guarded rules (P-12), in the plan's order.</summary>
    public static readonly IReadOnlyList<string> GuardedRules =
        [EnglishLabel, RawStatus, PrintedId, RawJson, InlineHandler, StyleAttribute, EmptyTable, UnformattedTime];

    /// <summary>
    /// D-67-7: abbreviations a label may carry after its Arabic name, in brackets («رمز الصنف (SKU)»). File formats and standard codes
    /// that have no Arabic word in use stay as they are.
    /// </summary>
    public static readonly IReadOnlySet<string> Abbreviations = new HashSet<string>(StringComparer.Ordinal)
    {
        "SKU", "COI", "FTE", "JSON", "PDF", "CSV", "XLSX", "QR", "OTP", "IBAN", "SMS", "KPI", "API", "URL", "IP", "SLA", "ISO", "HTTP", "UTC",
        "SHA", "ID", "HR", "IT", "TOTP", "MFA", "SSO", "OIDC", "CSP", "DPA", "NFC", "RFID", "PIN", "VAT", "GPS", "BCC", "CC", "ETL", "ZIP",
        "SoD", "Keycloak", "Excel",
    };

    /// <summary>
    /// The technical ids a page may print (P-6, §3 rule 1), each with its reason: the guard never lists them as rows. Matched against the
    /// printed member's name, in the files named.
    /// </summary>
    public static readonly IReadOnlyList<(string FileSuffix, string Member, string Reason)> TechnicalIds =
    [
        ("HRManagement.Gateway/Views/Home/Audit.cshtml", "CorrelationId", "the correlation id on the audit page: what an operator searches the logs with"),
    ];

    private static readonly string[] ExcludedDirectories = ["/bin/", "/obj/", "/node_modules/", "/.claude/", "/artifacts/"];

    /// <summary>Every <c>.cshtml</c> under <c>src/</c>, as repository-relative paths with forward slashes.</summary>
    public static IReadOnlyList<string> ViewFiles()
    {
        var root = RepoPaths.Root;
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cshtml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !ExcludedDirectories.Any(d => ("/" + f).Contains(d, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The web project a view belongs to (<c>HR.Core.Web</c>, <c>HRManagement.Gateway</c>, <c>HRManagement.Web.Shared</c>…).</summary>
    public static string ProjectOf(string relativePath)
    {
        var parts = relativePath.Split('/');
        var index = Array.FindIndex(parts, p => p.EndsWith(".Web", StringComparison.Ordinal) || p is "HRManagement.Gateway" or "HRManagement.Web.Shared");
        return index >= 0 ? parts[index] : parts.Length > 2 ? parts[^3] : "unknown";
    }

    /// <summary>Scans every view of the repository.</summary>
    public static IReadOnlyList<UiFinding> ScanRepository()
    {
        var all = new List<UiFinding>();
        foreach (var file in ViewFiles())
            all.AddRange(Scan(file, global::System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, file), Encoding.UTF8), reflectModels: true));
        return all;
    }

    /// <summary>Scans one view's text; <paramref name="relativePath"/> names it in the findings and their keys.</summary>
    /// <param name="reflectModels">Also check the labels a view takes from its model (asp-for, DisplayNameFor) by reflection.</param>
    public static IReadOnlyList<UiFinding> Scan(string relativePath, string text, bool reflectModels = false, Type? modelType = null)
    {
        var project = ProjectOf(relativePath);
        var clean = StripComments(text);
        var lines = new LineIndex(clean);
        var raw = new List<(int Index, string Rule, string Snippet)>();
        var blocks = CodeBlocks(clean);

        // 5, 6: inline handlers and style attributes, inside any markup tag.
        foreach (Match tag in TagRegex().Matches(clean))
        {
            if (InBlocks(blocks.Scripts, tag.Index) || InBlocks(blocks.Styles, tag.Index))
                continue;
            foreach (Match h in HandlerRegex().Matches(tag.Value))
                raw.Add((tag.Index + h.Index, InlineHandler, h.Value.Trim()));
            foreach (Match s in StyleAttrRegex().Matches(tag.Value))
                raw.Add((tag.Index + s.Index, StyleAttribute, Excerpt(tag.Value, s.Index, 60)));
            // A datetime-local input bound with asp-for is shown and read in local time by the shared tag helper and model binder
            // (HrDateTimeLocalTagHelper, HrLocalDateTimeModelBinder); one written by hand formats its value, which the format rule finds.
            if (DateTimeLocalRegex().IsMatch(tag.Value))
                raw.Add((tag.Index, DateTimeLocal, Excerpt(tag.Value, 0, 80)));
        }

        // Inventory: inline blocks.
        foreach (var (start, _) in blocks.Scripts)
            raw.Add((start, InlineScript, Excerpt(clean, start, 40)));
        foreach (var (start, _) in blocks.Styles)
            raw.Add((start, InlineStyle, Excerpt(clean, start, 40)));

        // 1: English labels, and (by reflection) the labels a view takes from its model.
        if (reflectModels || modelType is not null)
        {
            foreach (var (index, snippet) in EnglishModelLabels(relativePath, clean, modelType))
                raw.Add((index, EnglishLabel, snippet));
        }

        foreach (Match m in LabelElementRegex().Matches(clean))
        {
            if (InBlocks(blocks.Scripts, m.Index))
                continue;
            var visible = VisibleText(m.Groups["body"].Value);
            var words = EnglishWordRegex().Matches(visible).Select(w => w.Value).Where(w => !Abbreviations.Contains(w)).ToList();
            if (words.Count > 0)
                raw.Add((m.Index, EnglishLabel, $"<{m.Groups["tag"].Value.ToLowerInvariant()}> {Collapse(visible)}"));
        }

        // 2, 3, 4, 8: what Razor prints.
        foreach (var print in Prints(clean, blocks))
        {
            var member = print.Member;
            if (print.MethodCall && !print.ToStringCall)
                continue;
            if (print.ToStringCall && TimeMember().IsMatch(member))
                continue; // a formatted time: the format rule below reports it
            if (JsonMember().IsMatch(member) && !relativePath.EndsWith("/_TechnicalJson.cshtml", StringComparison.Ordinal) && !print.InTextarea)
                raw.Add((print.Index, RawJson, print.Snippet));
            else if (IdMember().IsMatch(member) && !IsTechnicalId(relativePath, member))
                raw.Add((print.Index, PrintedId, print.Snippet));
            else if (StatusMember().IsMatch(member) && !NotStatusMember().IsMatch(member))
                raw.Add((print.Index, RawStatus, print.Snippet));
            else if (TimeMember().IsMatch(member) && !NotTimeMember().IsMatch(member))
                raw.Add((print.Index, UnformattedTime, print.Snippet));
        }

        // 8: formatted dates and times.
        foreach (Match m in ToStringFormatRegex().Matches(clean))
        {
            if (InBlocks(blocks.Scripts, m.Index) || !IsDateFormat(m.Groups["fmt"].Value))
                continue;
            raw.Add((m.Index, UnformattedTime, Excerpt(clean, Math.Max(0, m.Index - 30), 30 + m.Length)));
        }

        foreach (Match m in OtherDateFormatRegex().Matches(clean))
        {
            if (InBlocks(blocks.Scripts, m.Index))
                continue;
            raw.Add((m.Index, UnformattedTime, Excerpt(clean, Math.Max(0, m.Index - 20), 20 + m.Length)));
        }

        // 7: tables filled by a loop with no empty message near them.
        foreach (Match table in TableRegex().Matches(clean))
        {
            if (!LoopRegex().IsMatch(table.Value))
                continue;
            var startLine = lines.LineOf(table.Index);
            var endLine = lines.LineOf(table.Index + table.Length);
            var region = lines.Slice(startLine - 25, endLine + 25);
            if (!EmptyMarkerRegex().IsMatch(region))
                raw.Add((table.Index, EmptyTable, Excerpt(clean, table.Index, 60)));
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        return raw
            .OrderBy(r => r.Index)
            .Select(r =>
            {
                var snippet = Collapse(r.Snippet);
                var baseKey = $"{relativePath}#{r.Rule}#{snippet}";
                var n = seen.TryGetValue(baseKey, out var c) ? c + 1 : 1;
                seen[baseKey] = n;
                return new UiFinding(project, relativePath, lines.LineOf(r.Index), r.Rule, snippet) { Key = n == 1 ? baseKey : $"{baseKey}#{n}" };
            })
            .ToList();
    }

    private static bool IsTechnicalId(string relativePath, string member) =>
        TechnicalIds.Any(t => relativePath.EndsWith(t.FileSuffix, StringComparison.Ordinal) && string.Equals(t.Member, member, StringComparison.Ordinal));

    /// <summary>A .NET custom or standard date/time format (not a numeric one: N2, 0.##, D for integers…).</summary>
    public static bool IsDateFormat(string format)
    {
        if (format.Length == 1)
            return "dgGuostTMYUfF".Contains(format, StringComparison.Ordinal) && format != "G" && format != "F";
        return DateTokenRegex().IsMatch(format);
    }

    // ------------------------------------------------------------------ Razor prints

    private sealed record Print(int Index, string Member, string Snippet, bool MethodCall, bool ToStringCall, bool InTextarea);

    private static readonly HashSet<string> RazorKeywords = new(StringComparer.Ordinal)
    {
        "if", "else", "foreach", "for", "while", "do", "switch", "using", "model", "inject", "section", "functions", "await", "try",
        "catch", "finally", "lock", "addTagHelper", "removeTagHelper", "tagHelperPrefix", "page", "namespace", "inherits", "implements",
        "layout", "code", "attribute", "preservewhitespace", "typeparam", "RenderBody", "RenderSection", "RenderSectionAsync", "Html", "Url",
        "Json", "ViewData", "ViewBag", "TempData", "Context", "Component", "Localizer"
    };

    /// <summary>
    /// What Razor prints in markup: implicit expressions (<c>@a.b.C</c>) and simple explicit ones (<c>@(a.b.C)</c>, <c>@(a.C ?? "—")</c>,
    /// <c>@($"…{a.C}…")</c>), with the last member's name. Prints inside a tag count only in visible attributes (title, placeholder,
    /// aria-label, alt); prints inside script and style blocks never count.
    /// </summary>
    private static IEnumerable<Print> Prints(string text, (List<(int, int)> Scripts, List<(int, int)> Styles, List<(int, int)> Textareas) blocks)
    {
        foreach (Match m in ImplicitRegex().Matches(text))
        {
            if (InBlocks(blocks.Scripts, m.Index) || InBlocks(blocks.Styles, m.Index) || !IsVisiblePosition(text, m.Index))
                continue;
            var chain = m.Groups["chain"].Value;
            var segments = chain.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd('?', '!')).ToArray();
            if (segments.Length < 2 || RazorKeywords.Contains(segments[0]))
                continue;
            var after = m.Index + m.Length < text.Length ? text[m.Index + m.Length] : ' ';
            var methodCall = after == '(';
            var toString = false;
            var member = segments[^1];
            if (methodCall && member == "ToString" && segments.Length >= 3)
            {
                toString = true;
                member = segments[^2];
            }

            yield return new Print(m.Index, member, Excerpt(text, m.Index, Math.Min(80, m.Length + 3)), methodCall, toString, InBlocks(blocks.Textareas, m.Index));
        }

        foreach (Match m in ExplicitStartRegex().Matches(text))
        {
            if (InBlocks(blocks.Scripts, m.Index) || InBlocks(blocks.Styles, m.Index) || !IsVisiblePosition(text, m.Index))
                continue;
            var body = BalancedParens(text, m.Index + 1);
            if (body is null)
                continue;
            var trimmed = body.Trim();
            var simple = SimpleExplicitRegex().Match(trimmed);
            var textarea = InBlocks(blocks.Textareas, m.Index);
            if (simple.Success)
            {
                var segments = simple.Groups["chain"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd('?', '!')).ToArray();
                if (segments.Length >= 2 && !RazorKeywords.Contains(segments[0]))
                {
                    var toString = simple.Groups["tostring"].Success;
                    yield return new Print(m.Index, segments[^1], Excerpt(text, m.Index, Math.Min(80, body.Length + 3)), false, toString, textarea);
                }

                continue;
            }

            if (trimmed.StartsWith("$\"", StringComparison.Ordinal) || trimmed.StartsWith("$@\"", StringComparison.Ordinal))
            {
                foreach (Match hole in InterpolationHoleRegex().Matches(trimmed))
                {
                    var segments = hole.Groups["chain"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd('?', '!')).ToArray();
                    if (segments.Length >= 2 && !RazorKeywords.Contains(segments[0]) && !hole.Groups["fmt"].Success)
                        yield return new Print(m.Index, segments[^1], Excerpt(text, m.Index, Math.Min(80, body.Length + 3)), false, false, textarea);
                }
            }
        }
    }

    /// <summary>False inside a tag, except in the value of an attribute the user sees (title, placeholder, aria-label, alt).</summary>
    private static bool IsVisiblePosition(string text, int index)
    {
        var lastOpen = text.LastIndexOf('<', index);
        var lastClose = text.LastIndexOf('>', index);
        if (lastOpen <= lastClose)
            return true;
        if (lastOpen + 1 < text.Length && !char.IsLetter(text[lastOpen + 1]))
            return true; // "<" in text or code ("a < b"), not a tag
        var inside = text[lastOpen..index];
        var attr = AttributeBeforeRegex().Match(inside);
        return attr.Success && VisibleAttributes.Contains(attr.Groups["name"].Value.ToLowerInvariant());
    }

    private static readonly HashSet<string> VisibleAttributes = new(StringComparer.Ordinal) { "title", "placeholder", "aria-label", "alt", "data-bs-title" };

    private static string? BalancedParens(string text, int openIndex)
    {
        if (openIndex >= text.Length || text[openIndex] != '(')
            return null;
        var depth = 0;
        var inString = false;
        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\'))
                inString = !inString;
            if (inString)
                continue;
            if (c == '(')
                depth++;
            else if (c == ')' && --depth == 0)
                return text.Substring(openIndex + 1, i - openIndex - 1);
            if (i - openIndex > 2000)
                return null;
        }

        return null;
    }

    // ------------------------------------------------------------------ helpers

    private static (List<(int, int)> Scripts, List<(int, int)> Styles, List<(int, int)> Textareas) CodeBlocks(string text)
    {
        static List<(int, int)> Blocks(string t, Regex open, string close, Func<Match, bool> keep)
        {
            var list = new List<(int, int)>();
            foreach (Match m in open.Matches(t))
            {
                if (!keep(m))
                    continue;
                var end = t.IndexOf(close, m.Index + m.Length, StringComparison.OrdinalIgnoreCase);
                list.Add((m.Index, end < 0 ? t.Length : end + close.Length));
            }

            return list;
        }

        return (
            Blocks(text, ScriptOpenRegex(), "</script>", m => !SrcAttrRegex().IsMatch(m.Value)),
            Blocks(text, StyleOpenRegex(), "</style>", _ => true),
            Blocks(text, TextareaOpenRegex(), "</textarea>", _ => true));
    }

    private static bool InBlocks(List<(int Start, int End)> blocks, int index) => blocks.Any(b => index >= b.Start && index < b.End);

    /// <summary>The text a label shows: nested tags, Razor expressions and entities removed.</summary>
    public static string VisibleText(string body)
    {
        var t = InternalCodeRegex().Replace(body, " ");
        t = InnerTagRegex().Replace(t, " ");
        t = RemoveRazorExpressions(t);
        t = ElseRegex().Replace(t, " ");
        t = EntityRegex().Replace(t, " ").Replace('{', ' ').Replace('}', ' ');
        return t;
    }

    private static readonly HashSet<string> ControlKeywords = new(StringComparer.Ordinal) { "if", "foreach", "for", "while", "switch", "using", "lock" };

    private static string RemoveRazorExpressions(string t)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] != '@')
            {
                sb.Append(t[i]);
                continue;
            }

            var j = i + 1;
            if (j < t.Length && t[j] == '(')
            {
                var body = BalancedParens(t, j);
                i = body is null ? t.Length : j + body.Length + 1;
                sb.Append(' ');
                continue;
            }

            var word = new string(t.Skip(j).TakeWhile(char.IsLetter).ToArray());
            if (ControlKeywords.Contains(word))
            {
                var k = j + word.Length;
                while (k < t.Length && char.IsWhiteSpace(t[k]))
                    k++;
                var body = BalancedParens(t, k);
                i = body is null ? k - 1 : k + body.Length + 1;
                sb.Append(' ');
                continue;
            }

            if (word == "await")
            {
                j += word.Length;
                while (j < t.Length && char.IsWhiteSpace(t[j]))
                    j++;
            }

            while (j < t.Length && (char.IsLetterOrDigit(t[j]) || t[j] is '_' or '.' or '?' or '!'))
            {
                j++;
                if (j < t.Length && t[j] == '(')
                {
                    var body = BalancedParens(t, j);
                    j = body is null ? t.Length : j + body.Length + 2;
                }
                else if (j < t.Length && t[j] == '[')
                {
                    var close = t.IndexOf(']', j);
                    j = close < 0 ? t.Length : close + 1;
                }
            }

            i = j - 1;
            sb.Append(' ');
        }

        return sb.ToString();
    }

    private static string StripComments(string text)
    {
        static string Blank(Match m) => new(m.Value.Select(c => c == '\n' ? '\n' : ' ').ToArray());
        var t = RazorCommentRegex().Replace(text, Blank);
        return HtmlCommentRegex().Replace(t, Blank);
    }

    private static string Excerpt(string text, int start, int length) =>
        text.Substring(start, Math.Min(length, text.Length - start));

    private static string Collapse(string s) =>
        WhitespaceRegex().Replace(s, " ").Trim().Replace('|', '¦');

    private sealed class LineIndex
    {
        private readonly List<int> _starts = [0];
        private readonly string _text;

        public LineIndex(string text)
        {
            _text = text;
            for (var i = 0; i < text.Length; i++)
                if (text[i] == '\n')
                    _starts.Add(i + 1);
        }

        public int LineOf(int index)
        {
            var i = _starts.BinarySearch(index);
            return (i >= 0 ? i : ~i - 1) + 1;
        }

        public string Slice(int fromLine, int toLine)
        {
            var from = _starts[Math.Clamp(fromLine, 1, _starts.Count) - 1];
            var toIdx = Math.Clamp(toLine, 1, _starts.Count);
            var to = toIdx >= _starts.Count ? _text.Length : _starts[toIdx];
            return _text[from..Math.Max(from, to)];
        }
    }

    // ------------------------------------------------------------------ patterns

    [GeneratedRegex(@"@\*.*?\*@", RegexOptions.Singleline)]
    private static partial Regex RazorCommentRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"<[a-zA-Z][\w:-]*(?:[^<>""']|""[^""]*""|'[^']*')*>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\son[a-z]{3,}\s*=\s*(""[^""]*""|'[^']*'|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex HandlerRegex();

    [GeneratedRegex(@"\sstyle\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttrRegex();

    [GeneratedRegex(@"type\s*=\s*[""']datetime-local[""']", RegexOptions.IgnoreCase)]
    private static partial Regex DateTimeLocalRegex();

    [GeneratedRegex(@"<script\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOpenRegex();

    [GeneratedRegex(@"\ssrc\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex SrcAttrRegex();

    [GeneratedRegex(@"<style\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleOpenRegex();

    [GeneratedRegex(@"<textarea\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex TextareaOpenRegex();

    [GeneratedRegex(@"<(?<tag>th|dt|label|button|legend|option|h[1-6])\b(?<attrs>(?:[^<>""']|""[^""]*""|'[^']*')*)>(?<body>.*?)</\k<tag>\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LabelElementRegex();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.Singleline)]
    private static partial Regex InnerTagRegex();

    /// <summary>P-5: an internal code shown as secondary text (<c>&lt;span class="hr-internal-code"&gt;D-POL-1&lt;/span&gt;</c>) is not part of the label.</summary>
    [GeneratedRegex(@"<(?<t>span|small|code)\b[^>]*\bclass\s*=\s*""[^""]*\bhr-internal-code\b[^""]*""[^>]*>.*?</\k<t>\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InternalCodeRegex();

    [GeneratedRegex(@"\belse\s+if\s*\([^)]*\)|\belse\b")]
    private static partial Regex ElseRegex();

    [GeneratedRegex(@"&[#\w]+;")]
    private static partial Regex EntityRegex();

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z]+\b")]
    private static partial Regex EnglishWordRegex();

    [GeneratedRegex(@"(?<![\w@])@(?<chain>[A-Za-z_]\w*(?:[?!]?\.[A-Za-z_]\w*)+)")]
    private static partial Regex ImplicitRegex();

    [GeneratedRegex(@"(?<![\w@])@\(")]
    private static partial Regex ExplicitStartRegex();

    [GeneratedRegex(@"^(?<chain>[A-Za-z_]\w*(?:[?!]?\.[A-Za-z_]\w*)+)(?<tostring>\??\.ToString\([^()]*\)(?:\[[^\]]*\])?)?(?:\s*\?\?\s*(?:""[^""]*""|[\w.?]+(?:\??\.ToString\([^()]*\))?))*$")]
    private static partial Regex SimpleExplicitRegex();

    [GeneratedRegex(@"\{(?<chain>[A-Za-z_]\w*(?:[?!]?\.[A-Za-z_]\w*)+)(?<fmt>:[^}]*)?\}")]
    private static partial Regex InterpolationHoleRegex();

    [GeneratedRegex(@"(?<name>[\w:-]+)\s*=\s*[""']?[^""'<>]*$")]
    private static partial Regex AttributeBeforeRegex();

    /// <summary>
    /// A status or a type (P-12 rule 2). <c>…Category</c> and <c>…Decision</c> are not in the rule: in this codebase they are text the user
    /// types (an asset's category, a course's category, an appeal's decision); the coded ones (a fraud alert's category, an offer's
    /// decline category) print through their Arabic names all the same.
    /// </summary>
    [GeneratedRegex(@"^(?:\w*Status|\w*State|\w*Type|\w*Kind|\w*Mode|Scope|\w*Stage|\w*Outcome|\w*Severity)$")]
    private static partial Regex StatusMember();

    /// <summary>Members that end like a status but carry a name, a count or text (counts, names, descriptions).</summary>
    [GeneratedRegex(@"(?:Count|Name|NameAr|Label|Description|Text|Note|Notes|Code)$")]
    private static partial Regex NotStatusMember();

    [GeneratedRegex(@"^(?:Id|\w+Id|\w+By|UserId|\w+UserSub|Sub)$")]
    private static partial Regex IdMember();

    [GeneratedRegex(@"Json$")]
    private static partial Regex JsonMember();

    [GeneratedRegex(@"^(?:\w*At|\w*AtUtc|\w*Utc|\w*Date|\w*DateUtc|\w*Time|\w*TimeUtc|\w*Timestamp|\w+On)$")]
    private static partial Regex TimeMember();

    /// <summary>Members that end like a time but are not one (a reason, a count of days, a flag).</summary>
    [GeneratedRegex(@"(?:Count|Days|Hours|Minutes|Name|Reason|Note|Notes|Description|Type|Kind|Mode|Status|Zone|Is\w+|Has\w+)$")]
    private static partial Regex NotTimeMember();

    [GeneratedRegex(@"\.ToString\(\s*""(?<fmt>[^""]*)""")]
    private static partial Regex ToStringFormatRegex();

    [GeneratedRegex(@"(?:yy|MM|dd|HH|hh|mm)")]
    private static partial Regex DateTokenRegex();

    [GeneratedRegex(@"\.To(?:Short|Long)(?:Date|Time)String\(\)|\.ToLocalTime\(\)|\{[^{}""]+:(?:yyyy|yy|dd|MM|HH)[^{}""]*\}|asp-format\s*=\s*""[^""]*(?:yyyy|dd|HH)[^""]*""")]
    private static partial Regex OtherDateFormatRegex();

    [GeneratedRegex(@"<table\b.*?</table\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"@foreach\s*\(|@for\s*\(")]
    private static partial Regex LoopRegex();

    [GeneratedRegex(@"_HrEmptyState|HrEmptyState|\.Any\(\)|Count\s*(?:==|<=|<|>|!=)\s*0|Count\(\)\s*(?:==|>|!=)\s*0|Length\s*(?:==|>|!=)\s*0|\{\s*Count\s*:\s*0\s*\}|IsEmpty|لا توجد|لا يوجد|لا تتوفر|لا يتوفر|لا سجلات|لا نتائج|لا عناصر|لا بيانات")]
    private static partial Regex EmptyMarkerRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
