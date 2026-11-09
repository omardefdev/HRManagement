using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HRManagement.SharedKernel.CodeGeneration;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

/// <summary>
/// PLAN-48 (§3): a code policy version's number format, built from its segments. The one place that composes a code and parses
/// one back (the generator, the counter reseeding and the migration bootstrap all use it), so a code the generator can compose is
/// always one the reseeder can read.
/// </summary>
public sealed class CodeFormat
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Regex _pattern;

    public CodeFormat(IReadOnlyList<CodeSegmentDefinition> segments, CodeSequenceReset reset)
    {
        Segments = segments;
        Reset = reset;
        SequenceSegment = segments.FirstOrDefault(s => s.Kind == CodeSegmentKind.Sequence);
        ScopeFields = SequenceSegment?.ScopeFields?.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToArray() ?? [];
        _pattern = BuildPattern(segments, knownFacts: null);
    }

    public IReadOnlyList<CodeSegmentDefinition> Segments { get; }

    public CodeSequenceReset Reset { get; }

    public CodeSegmentDefinition? SequenceSegment { get; }

    /// <summary>The facts the counter is kept per (the sequence segment's scope fields).</summary>
    public IReadOnlyList<string> ScopeFields { get; }

    public bool HasSequence => SequenceSegment is not null;

    /// <summary>A date segment that shows the year (YYYY, YY or yyyyMMdd).</summary>
    public bool ShowsYear => Segments.Any(s => s.Kind == CodeSegmentKind.Date && YearFormat(s.Value) is not null);

    /// <summary>The fact names the code shows (Field, ParentCode and LookupPrefix segments).</summary>
    public IEnumerable<string> FieldNames => Segments.Where(IsFieldSegment).Select(s => s.Value.Trim());

    /// <summary>The leading literal of the code (its declared prefix), or empty when the code starts with a fact.</summary>
    public string LeadingLiteral => Segments.Count > 0 && Segments[0].Kind == CodeSegmentKind.Literal ? Segments[0].Value : string.Empty;

    public static CodeFormat FromJson(string segmentsJson, CodeSequenceReset reset) =>
        new(ParseSegments(segmentsJson), reset);

    public static IReadOnlyList<CodeSegmentDefinition> ParseSegments(string segmentsJson) =>
        JsonSerializer.Deserialize<List<CodeSegmentDefinition>>(string.IsNullOrWhiteSpace(segmentsJson) ? "[]" : segmentsJson, JsonOptions) ?? [];

    public static bool IsFieldSegment(CodeSegmentDefinition segment) =>
        segment.Kind is CodeSegmentKind.Field or CodeSegmentKind.ParentCode or CodeSegmentKind.LookupPrefix;

    /// <summary>The values of the scope fields in <paramref name="facts"/>, in the order of the scope; a missing one is refused.</summary>
    public IReadOnlyList<string> ScopeValues(IReadOnlyDictionary<string, string> facts) =>
        ScopeFields.Select(field => RequireFact(facts, field)).ToArray();

    /// <summary>
    /// The code for <paramref name="facts"/> and <paramref name="sequence"/> at <paramref name="nowUtc"/>. A fact the code shows
    /// and the request does not carry is refused (<see cref="CodeFactMissingException"/>): it never becomes an empty segment.
    /// </summary>
    public string Compose(IReadOnlyDictionary<string, string> facts, long sequence, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        foreach (var seg in Segments)
        {
            sb.Append(seg.Kind switch
            {
                CodeSegmentKind.Literal => seg.Value,
                CodeSegmentKind.Field or CodeSegmentKind.ParentCode or CodeSegmentKind.LookupPrefix => RequireFact(facts, seg.Value.Trim()),
                CodeSegmentKind.Date => FormatDate(seg.Value, nowUtc),
                CodeSegmentKind.Sequence => sequence.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(seg.PadWidth, 1), '0'),
                _ => string.Empty
            });
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reads <paramref name="code"/> back: the scope values (in the order of <see cref="ScopeFields"/>), the sequence and the year
    /// the code shows. With <paramref name="knownFacts"/> (the facts of an issuance, or an entity's columns) the fields it carries
    /// are matched as written, which removes the ambiguity of a field whose value holds a separator.
    /// </summary>
    public bool TryParse(
        string? code,
        out IReadOnlyList<string> scopeValues,
        out long sequence,
        out int? year,
        IReadOnlyDictionary<string, string>? knownFacts = null)
    {
        scopeValues = [];
        sequence = 0;
        year = null;
        if (string.IsNullOrWhiteSpace(code) || !HasSequence)
            return false;

        var pattern = knownFacts is null ? _pattern : BuildPattern(Segments, knownFacts);
        var match = pattern.Match(code.Trim());
        if (!match.Success)
            return false;

        if (!long.TryParse(match.Groups["seq"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out sequence))
            return false;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Segments.Count; i++)
        {
            var seg = Segments[i];
            if (!IsFieldSegment(seg))
                continue;
            var name = seg.Value.Trim();
            var value = knownFacts is not null && knownFacts.TryGetValue(name, out var known) && !string.IsNullOrWhiteSpace(known)
                ? known.Trim()
                : match.Groups[$"f{i}"].Value;
            fields.TryAdd(name, value);
        }

        var values = new List<string>();
        foreach (var field in ScopeFields)
        {
            if (!fields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
                return false;
            values.Add(value);
        }

        scopeValues = values;
        year = ReadYear(match);
        return true;
    }

    private static Regex BuildPattern(IReadOnlyList<CodeSegmentDefinition> segments, IReadOnlyDictionary<string, string>? knownFacts)
    {
        var sb = new StringBuilder("^");
        var dateIndex = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            switch (seg.Kind)
            {
                case CodeSegmentKind.Literal:
                    sb.Append(Regex.Escape(seg.Value));
                    break;
                case CodeSegmentKind.Field or CodeSegmentKind.ParentCode or CodeSegmentKind.LookupPrefix:
                    if (knownFacts is not null && knownFacts.TryGetValue(seg.Value.Trim(), out var known) && !string.IsNullOrWhiteSpace(known))
                        sb.Append(Regex.Escape(known.Trim()));
                    else
                        sb.Append($"(?<f{i}>.+?)");
                    break;
                case CodeSegmentKind.Date:
                    var group = $"d{dateIndex++}";
                    sb.Append(YearFormat(seg.Value) switch
                    {
                        "YYYY" => $"(?<{group}_YYYY>\\d{{4}})",
                        "YY" => $"(?<{group}_YY>\\d{{2}})",
                        "yyyyMMdd" => $"(?<{group}_yyyyMMdd>\\d{{8}})",
                        _ => ".+?"
                    });
                    break;
                case CodeSegmentKind.Sequence:
                    sb.Append($"(?<seq>\\d{{{Math.Max(seg.PadWidth, 1)},}})");
                    break;
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
    }

    private static int? ReadYear(Match match)
    {
        foreach (Group group in match.Groups)
        {
            if (!group.Success || !group.Name.StartsWith('d'))
                continue;
            if (group.Name.EndsWith("_YYYY", StringComparison.Ordinal))
                return int.Parse(group.Value, CultureInfo.InvariantCulture);
            if (group.Name.EndsWith("_YY", StringComparison.Ordinal))
                return 2000 + int.Parse(group.Value, CultureInfo.InvariantCulture);
            if (group.Name.EndsWith("_yyyyMMdd", StringComparison.Ordinal))
                return int.Parse(group.Value[..4], CultureInfo.InvariantCulture);
        }

        return null;
    }

    /// <summary>YYYY, YY or yyyyMMdd when the date format shows the year; null otherwise.</summary>
    public static string? YearFormat(string? format) =>
        format switch
        {
            "YYYY" => "YYYY",
            "YY" => "YY",
            "yyyyMMdd" => "yyyyMMdd",
            _ => null
        };

    public static string FormatDate(string format, DateTime nowUtc) =>
        format switch
        {
            "YYYY" => nowUtc.Year.ToString("D4", CultureInfo.InvariantCulture),
            "YY" => (nowUtc.Year % 100).ToString("D2", CultureInfo.InvariantCulture),
            "yyyyMMdd" => nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            _ => nowUtc.ToString(format, CultureInfo.InvariantCulture)
        };

    private static string RequireFact(IReadOnlyDictionary<string, string> facts, string name) =>
        facts.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new CodeFactMissingException(name);
}

/// <summary>PLAN-48: a fact the code shows (or its counter is kept by) is missing from the request: a 422, never an empty segment.</summary>
public sealed class CodeFactMissingException(string factName)
    : InvalidOperationException($"The code generation request lacks the fact '{factName}' that the number shows.")
{
    public string FactName { get; } = factName;

    public string MessageAr => $"طلب الترميز ينقصه الحقل «{FactName}» الذي يظهر في الرقم";
}
