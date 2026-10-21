using System.Text.Json;
using System.Text.Json.Serialization;

namespace HRManagement.SharedKernel.Legal;

/// <summary>GOV-002: read-only legal decision ids for Article 159 resolve UI.</summary>
public static class LegalDecisionRegisterProvider
{
    public sealed record LegalDecisionEntry(string DecisionId, string? OfficialSource);

    private sealed class RegisterRoot
    {
        [JsonPropertyName("decisions")]
        public List<DecisionDto> Decisions { get; set; } = [];
    }

    private sealed class DecisionDto
    {
        [JsonPropertyName("decisionId")]
        public string DecisionId { get; set; } = "";

        [JsonPropertyName("officialSource")]
        public string? OfficialSource { get; set; }
    }

    private static readonly Lazy<IReadOnlyList<LegalDecisionEntry>> Decisions = new(Load);

    public static IReadOnlyList<LegalDecisionEntry> ListAll() => Decisions.Value;

    private static IReadOnlyList<LegalDecisionEntry> Load()
    {
        foreach (var path in ResolvePaths())
        {
            if (!File.Exists(path))
                continue;

            var json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<RegisterRoot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return root?.Decisions
                       .Where(d => !string.IsNullOrWhiteSpace(d.DecisionId))
                       .Select(d => new LegalDecisionEntry(d.DecisionId, d.OfficialSource))
                       .ToList()
                   ?? [];
        }

        return [];
    }

    private static IEnumerable<string> ResolvePaths()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            yield return Path.Combine(dir, "docs", "legal-decisions", "register.json");
            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;
            dir = parent.FullName;
        }
    }
}
