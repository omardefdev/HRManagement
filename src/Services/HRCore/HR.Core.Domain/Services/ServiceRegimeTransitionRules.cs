using System.Text.Json;
using System.Text.Json.Serialization;

namespace HR.Core.Domain.Services;

/// <summary>SYR-007: allowed regime transitions from service-regimes.json.</summary>
public static class ServiceRegimeTransitionRules
{
    public const string DecisionRefRequiredMessage = "مرجع القرار/المرسوم مطلوب لانتقال النظام.";
    public const string TransitionNotAllowedMessage = "انتقال النظام غير مسموح بين هذين النظامين.";
    public const string SameRegimeMessage = "نظام الخدمة الهدف مطابق للنظام الحالي.";
    public const string EmploymentRecordRequiredMessage = "لا يوجد سجل وظيفي نشط للموظف.";

    private sealed class TransitionPack
    {
        [JsonPropertyName("transition_rules")]
        public TransitionRulesDto? TransitionRules { get; set; }
    }

    private sealed class TransitionRulesDto
    {
        [JsonPropertyName("allowed_transitions")]
        public List<AllowedTransitionDto> AllowedTransitions { get; set; } = [];
    }

    private sealed class AllowedTransitionDto
    {
        [JsonPropertyName("from")]
        public string From { get; set; } = "";

        [JsonPropertyName("to")]
        public string To { get; set; } = "";

        [JsonPropertyName("condition")]
        public string Condition { get; set; } = "";
    }

    private static readonly Lazy<IReadOnlyList<(string From, string To, string Condition)>> AllowedTransitions =
        new(LoadAllowedTransitions);

    public static bool IsAllowed(string? fromRegimeId, string toRegimeId, string? conditionCode)
    {
        if (string.IsNullOrWhiteSpace(toRegimeId))
            return false;

        var from = fromRegimeId ?? string.Empty;
        return AllowedTransitions.Value.Any(t =>
            string.Equals(t.From, from, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.To, toRegimeId, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(conditionCode)
                || string.Equals(t.Condition, conditionCode, StringComparison.OrdinalIgnoreCase)));
    }

    public static IReadOnlyList<(string From, string To, string Condition)> GetAllowedTransitions() =>
        AllowedTransitions.Value;

    private static IReadOnlyList<(string From, string To, string Condition)> LoadAllowedTransitions()
    {
        foreach (var path in ResolvePackPaths())
        {
            if (!File.Exists(path))
                continue;

            var json = File.ReadAllText(path);
            var pack = JsonSerializer.Deserialize<TransitionPack>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return pack?.TransitionRules?.AllowedTransitions
                       .Select(t => (t.From, t.To, t.Condition))
                       .ToList()
                   ?? [];
        }

        return [];
    }

    private static IEnumerable<string> ResolvePackPaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "seed-packs", "syr003-service-regimes-v1", "regimes.json");

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            yield return Path.Combine(dir, "seed-packs", "syr003-service-regimes-v1", "regimes.json");
            yield return Path.Combine(dir, "docs", "syr", "service-regimes.json");
            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;
            dir = parent.FullName;
        }
    }
}
