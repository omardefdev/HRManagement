using System.Text.Json;
using System.Text.Json.Serialization;

namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-009: promotion rules per service regime from service-regimes.json.</summary>
public static class ServiceRegimePromotionRules
{
    public const string DecisionRefRequiredMessage = "مرجع القرار/المرسوم مطلوب للترفيع.";
    public const string RegimeRulesNotFoundMessage = "لا توجد قواعد ترفيع مهيأة لهذا النظام.";
    public const string InvalidGradeStepMessage = "درجة/مرتبة الهدف يجب أن تكون أعلى من الحالية.";

    public sealed record PromotionRule(
        string Regime,
        int MinTenureMonths,
        bool RequiresVacancy,
        string BonusCode,
        string GradeStructure);

    private sealed class PromotionPack
    {
        [JsonPropertyName("promotion_rules")]
        public List<PromotionRuleDto> PromotionRules { get; set; } = [];
    }

    private sealed class PromotionRuleDto
    {
        [JsonPropertyName("regime")]
        public string Regime { get; set; } = "";

        [JsonPropertyName("min_tenure_months")]
        public int MinTenureMonths { get; set; }

        [JsonPropertyName("requires_vacancy")]
        public bool RequiresVacancy { get; set; }

        [JsonPropertyName("bonus_code")]
        public string BonusCode { get; set; } = "";

        [JsonPropertyName("grade_structure")]
        public string GradeStructure { get; set; } = "";
    }

    private static readonly Lazy<IReadOnlyDictionary<string, PromotionRule>> Rules =
        new(LoadRules);

    public static PromotionRule? GetRules(string? regimeId)
    {
        if (string.IsNullOrWhiteSpace(regimeId))
            return null;

        return Rules.Value.TryGetValue(regimeId, out var rule) ? rule : null;
    }

    public static string? GetBonusCode(string? regimeId) => GetRules(regimeId)?.BonusCode;

    public static string? ValidatePromotion(
        string? regimeId,
        int fromGrade,
        int toGrade,
        int fromStep,
        int toStep,
        string? transitionDecisionRef)
    {
        if (string.IsNullOrWhiteSpace(transitionDecisionRef))
            return DecisionRefRequiredMessage;

        var rules = GetRules(regimeId);
        if (rules is null)
            return RegimeRulesNotFoundMessage;

        if (toGrade < fromGrade || (toGrade == fromGrade && toStep <= fromStep))
            return InvalidGradeStepMessage;

        return null;
    }

    private static IReadOnlyDictionary<string, PromotionRule> LoadRules() => Parse(ServiceRegimePack.CurrentJson);

    /// <summary>PLAN-37: the promotion rules this reader can use from <paramref name="json"/> (the readiness check counts these).</summary>
    internal static IReadOnlyDictionary<string, PromotionRule> Parse(string? json)
    {
        // PLAN-37: one source for the pack (ServiceRegimePack); an absent pack fails readiness, it is not silent.
        if (json is null)
            return new Dictionary<string, PromotionRule>(StringComparer.OrdinalIgnoreCase);

        var pack = JsonSerializer.Deserialize<PromotionPack>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return pack?.PromotionRules
                   .Where(r => !string.IsNullOrWhiteSpace(r.Regime))
                   .ToDictionary(
                       r => r.Regime,
                       r => new PromotionRule(
                           r.Regime,
                           r.MinTenureMonths,
                           r.RequiresVacancy,
                           r.BonusCode,
                           r.GradeStructure),
                       StringComparer.OrdinalIgnoreCase)
               ?? new Dictionary<string, PromotionRule>(StringComparer.OrdinalIgnoreCase);
    }
}
