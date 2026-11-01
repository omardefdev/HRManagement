using System.Text.Json;
using System.Text.Json.Serialization;

namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-009: Article 159 conflict detection from service-regimes.json.</summary>
public static class ServiceRegimeArticle159Rules
{
    public const string LegalDecisionRequiredMessage = "تعارض المادة 159 يتطلب ربط قرار قانوني (GOV-002).";
    public const string Article159ConflictReason = "تعارض محتمل بموجب المادة 159 — يتطلب قراراً قانونياً.";

    public sealed record Article159Pattern(
        string Pattern,
        string Trigger,
        bool RequiresLegalDecision);

    public sealed record Article159Conflict(
        string Pattern,
        string FromRegimeId,
        string ToRegimeId,
        string EntitlementsJson);

    private sealed class Article159Pack
    {
        [JsonPropertyName("article_159_patterns")]
        public List<Article159PatternDto> Patterns { get; set; } = [];
    }

    private sealed class Article159PatternDto
    {
        [JsonPropertyName("pattern")]
        public string Pattern { get; set; } = "";

        [JsonPropertyName("trigger")]
        public string Trigger { get; set; } = "";

        [JsonPropertyName("requires_legal_decision")]
        public bool RequiresLegalDecision { get; set; }
    }

    private static readonly Lazy<IReadOnlyList<Article159Pattern>> Patterns = new(LoadPatterns);

    public static bool RequiresLegalDecision(string? fromRegimeId, string? toRegimeId)
    {
        var conflict = DetectConflict(fromRegimeId, toRegimeId);
        return conflict is not null
               && Patterns.Value.Any(p =>
                   p.RequiresLegalDecision
                   && string.Equals(p.Trigger, "transition_with_special_regime", StringComparison.OrdinalIgnoreCase));
    }

    public static Article159Conflict? DetectConflict(string? fromRegimeId, string? toRegimeId)
    {
        if (string.IsNullOrWhiteSpace(fromRegimeId) || string.IsNullOrWhiteSpace(toRegimeId))
            return null;

        if (string.Equals(fromRegimeId, toRegimeId, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!IsSpecialRegime(toRegimeId) && !IsSpecialRegime(fromRegimeId))
            return null;

        var pattern = Patterns.Value.FirstOrDefault(p =>
            string.Equals(p.Trigger, "transition_with_special_regime", StringComparison.OrdinalIgnoreCase));

        if (pattern is null)
            return null;

        var payload = JsonSerializer.Serialize(new
        {
            fromRegimeId,
            toRegimeId,
            overlap = "entitlement_review_required"
        });

        return new Article159Conflict(pattern.Pattern, fromRegimeId, toRegimeId, payload);
    }

    public static bool IsSpecialRegime(string regimeId) =>
        regimeId.StartsWith("special_", StringComparison.OrdinalIgnoreCase)
        || string.Equals(regimeId, "special_banking", StringComparison.OrdinalIgnoreCase)
        || string.Equals(regimeId, "special_judiciary", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<Article159Pattern> LoadPatterns() => Parse(ServiceRegimePack.CurrentJson);

    /// <summary>PLAN-37: the patterns this reader can use from <paramref name="json"/> (the readiness check counts these).</summary>
    internal static IReadOnlyList<Article159Pattern> Parse(string? json)
    {
        // PLAN-37: one source for the pack (ServiceRegimePack); an absent pack fails readiness, it is not silent.
        if (json is null)
            return [];

        var pack = JsonSerializer.Deserialize<Article159Pack>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return pack?.Patterns
                   .Where(p => !string.IsNullOrWhiteSpace(p.Pattern))
                   .Select(p => new Article159Pattern(p.Pattern, p.Trigger, p.RequiresLegalDecision))
                   .ToList()
               ?? [];
    }
}
