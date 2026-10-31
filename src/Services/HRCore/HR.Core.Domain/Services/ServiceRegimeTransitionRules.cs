using System.Text.Json;
using HRManagement.SharedKernel.Legal;
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
        // PLAN-37: one source for the pack (ServiceRegimePack); an absent pack fails readiness, it is not silent.
        var json = ServiceRegimePack.Current?.Json;
        if (json is not null)
        {
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

}
