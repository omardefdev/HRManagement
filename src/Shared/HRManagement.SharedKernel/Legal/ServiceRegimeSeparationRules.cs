using System.Text.Json;
using System.Text.Json.Serialization;

namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-009: separation type rules per service regime from service-regimes.json.</summary>
public static class ServiceRegimeSeparationRules
{
    public const string SeparationTypeNotAllowedMessage = "نوع إنهاء الخدمة غير مسموح لنظام الخدمة الحالي.";
    public const string RegimeRulesNotFoundMessage = "لا توجد قواعد فصل مهيأة لهذا النظام.";

    public sealed record SeparationRule(
        string Regime,
        IReadOnlyList<string> AllowedTypes,
        int NoticeDays,
        string PensionHandoff);

    private sealed class SeparationPack
    {
        [JsonPropertyName("separation_rules")]
        public List<SeparationRuleDto> SeparationRules { get; set; } = [];
    }

    private sealed class SeparationRuleDto
    {
        [JsonPropertyName("regime")]
        public string Regime { get; set; } = "";

        [JsonPropertyName("allowed_types")]
        public List<string> AllowedTypes { get; set; } = [];

        [JsonPropertyName("notice_days")]
        public int NoticeDays { get; set; }

        [JsonPropertyName("pension_handoff")]
        public string PensionHandoff { get; set; } = "";
    }

    private static readonly Lazy<IReadOnlyDictionary<string, SeparationRule>> Rules =
        new(LoadRules);

    public static SeparationRule? GetRules(string? regimeId)
    {
        if (string.IsNullOrWhiteSpace(regimeId))
            return null;

        return Rules.Value.TryGetValue(regimeId, out var rule) ? rule : null;
    }

    public static string? GetPensionAuthority(string? regimeId) => GetRules(regimeId)?.PensionHandoff;

    public static bool IsAllowedType(string? regimeId, string separationTypeCode)
    {
        var rules = GetRules(regimeId);
        if (rules is null)
            return false;

        return rules.AllowedTypes.Any(t =>
            string.Equals(t, separationTypeCode, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ValidateSeparationType(string? regimeId, string separationTypeCode)
    {
        if (string.IsNullOrWhiteSpace(regimeId))
            return RegimeRulesNotFoundMessage;

        var rules = GetRules(regimeId);
        if (rules is null)
            return RegimeRulesNotFoundMessage;

        if (!IsAllowedType(regimeId, separationTypeCode))
            return SeparationTypeNotAllowedMessage;

        return null;
    }

    /// <summary>Maps HR.Contracts SeparationType enum name to JSON code.</summary>
    public static string ToSeparationTypeCode(string separationTypeName) =>
        separationTypeName switch
        {
            "Resignation" => "resignation",
            "Termination" => "termination",
            "EndOfContract" => "contract_end",
            "Retirement" => "retirement",
            "Medical" => "medical",
            "Judicial" => "judicial",
            "Disciplinary" => "disciplinary",
            _ => separationTypeName.ToLowerInvariant()
        };

    private static IReadOnlyDictionary<string, SeparationRule> LoadRules()
    {
        // PLAN-37: one source for the pack (ServiceRegimePack); an absent pack fails readiness, it is not silent.
        var json = ServiceRegimePack.CurrentJson;
        if (json is null)
            return new Dictionary<string, SeparationRule>(StringComparer.OrdinalIgnoreCase);

        var pack = JsonSerializer.Deserialize<SeparationPack>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return pack?.SeparationRules
                   .Where(r => !string.IsNullOrWhiteSpace(r.Regime))
                   .ToDictionary(
                       r => r.Regime,
                       r => new SeparationRule(
                           r.Regime,
                           r.AllowedTypes,
                           r.NoticeDays,
                           r.PensionHandoff),
                       StringComparer.OrdinalIgnoreCase)
               ?? new Dictionary<string, SeparationRule>(StringComparer.OrdinalIgnoreCase);
    }
}
