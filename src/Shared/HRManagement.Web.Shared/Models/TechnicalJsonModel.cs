using System.Text.Json;

namespace HRManagement.Web.Shared.Models;

/// <summary>
/// PLAN-67 (§5, D-67-4, G-UX-06): raw JSON, shown to specialists only, folded under «عرض تقني» (the <c>_TechnicalJson</c> partial). The
/// interface guard allows a printed <c>…Json</c> nowhere else.
/// </summary>
public sealed record TechnicalJsonModel(IReadOnlyList<(string Label, string? Json)> Parts, string Summary = TechnicalJsonModel.DefaultSummary)
{
    public const string DefaultSummary = "عرض تقني";

    public TechnicalJsonModel(string? json, string summary = DefaultSummary)
        : this([("", json)], summary)
    {
    }

    /// <summary>The JSON indented when it parses; as stored otherwise (the page never fails on a malformed value).</summary>
    public static string Pretty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "—";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
