using System.Text.Json;
using System.Text.Json.Serialization;

namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-008: movement type rules from service-regimes.json.</summary>
public static class ServiceRegimeMovementRules
{
    public const string RegimeChangeNotAllowedMessage = "الانتداب/الإعارة/الندب لا تغيّر نظام الخدمة.";
    public const string CrossRegimeDecisionRequiredMessage = "النقل بين نظامين مختلفين يتطلب مرجع قرار/مرسوم.";
    public const string UnknownMovementTypeMessage = "نوع الحركة غير معروف في سجل أنظمة الخدمة.";

    public sealed record MovementTypeRule(
        string Code,
        string NameAr,
        string NameEn,
        bool PreservesRegime,
        bool MayChangeRegime);

    private sealed class MovementPack
    {
        [JsonPropertyName("movement_types")]
        public List<MovementTypeDto> MovementTypes { get; set; } = [];
    }

    private sealed class MovementTypeDto
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("name_ar")]
        public string NameAr { get; set; } = "";

        [JsonPropertyName("name_en")]
        public string NameEn { get; set; } = "";

        [JsonPropertyName("preserves_regime")]
        public bool? PreservesRegime { get; set; }

        [JsonPropertyName("may_change_regime")]
        public bool? MayChangeRegime { get; set; }
    }

    private static readonly Lazy<IReadOnlyDictionary<string, MovementTypeRule>> MovementTypes =
        new(LoadMovementTypes);

    public static bool PreservesRegime(string movementCode) =>
        TryGet(movementCode, out var rule) && rule.PreservesRegime;

    public static bool MayChangeRegime(string movementCode) =>
        TryGet(movementCode, out var rule) && rule.MayChangeRegime;

    public static bool TryGet(string movementCode, out MovementTypeRule rule)
    {
        rule = null!;
        if (string.IsNullOrWhiteSpace(movementCode))
            return false;

        return MovementTypes.Value.TryGetValue(movementCode, out rule!);
    }

    public static IReadOnlyList<MovementTypeRule> GetAll() => MovementTypes.Value.Values.ToList();

    public static string? ValidateRegimeChangeFields(string movementCode, string? targetRegimeId, string? decisionRef)
    {
        if (!TryGet(movementCode, out var rule))
            return UnknownMovementTypeMessage;

        if (rule.PreservesRegime && !string.IsNullOrWhiteSpace(targetRegimeId))
            return RegimeChangeNotAllowedMessage;

        if (rule.MayChangeRegime
            && !string.IsNullOrWhiteSpace(targetRegimeId)
            && string.IsNullOrWhiteSpace(decisionRef))
            return CrossRegimeDecisionRequiredMessage;

        return null;
    }

    public static string? ValidateCrossRegimeDecisionRequired(string? sourceRegimeId, string? targetRegimeId, string? decisionRef)
    {
        if (string.IsNullOrWhiteSpace(targetRegimeId)
            || string.Equals(sourceRegimeId, targetRegimeId, StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrWhiteSpace(decisionRef))
            return CrossRegimeDecisionRequiredMessage;

        return null;
    }

    private static IReadOnlyDictionary<string, MovementTypeRule> LoadMovementTypes() => Parse(ServiceRegimePack.CurrentJson);

    /// <summary>PLAN-37: the movement types this reader can use from <paramref name="json"/> (the readiness check counts these).</summary>
    internal static IReadOnlyDictionary<string, MovementTypeRule> Parse(string? json)
    {
        // PLAN-37: one source for the pack (ServiceRegimePack); an absent pack fails readiness, it is not silent.
        if (json is not null)
        {
            var pack = JsonSerializer.Deserialize<MovementPack>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return pack?.MovementTypes
                       .Where(m => !string.IsNullOrWhiteSpace(m.Code))
                       .ToDictionary(
                           m => m.Code,
                           m => new MovementTypeRule(
                               m.Code,
                               m.NameAr,
                               m.NameEn,
                               m.PreservesRegime ?? false,
                               m.MayChangeRegime ?? false),
                           StringComparer.OrdinalIgnoreCase)
                   ?? new Dictionary<string, MovementTypeRule>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, MovementTypeRule>(StringComparer.OrdinalIgnoreCase);
    }

}
