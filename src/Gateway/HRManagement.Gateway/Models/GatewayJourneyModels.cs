namespace HRManagement.Gateway.Models;

public sealed class GatewayJourneyDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required IReadOnlyList<GatewayJourneyStep> Steps { get; init; }
}

public sealed class GatewayJourneyStep
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? ModuleSlug { get; init; }
    /// <summary>مسار UI مخصص بدل الصفحة الافتراضية للوحدة.</summary>
    public string? UiPath { get; init; }
    /// <summary>مفتاح المرحلة للتجميع البصري (مثل recruitment / onboarding).</summary>
    public string? PhaseKey { get; init; }
    /// <summary>عنوان المرحلة بالعربية — يُعرض عند بداية مجموعة خطوات.</summary>
    public string? PhaseTitleAr { get; init; }
    /// <summary>مسار عام على البوابة (مثل /careers) خارج /ui/{module}/.</summary>
    public string? GatewayPublicPath { get; init; }
    public bool IsRequired { get; init; } = true;
    public string? Note { get; init; }
}

public sealed class GatewayJourneyDetailsViewModel
{
    public required GatewayJourneyDefinition Journey { get; init; }
    public required IReadOnlyList<GatewayJourneyStepStatus> Steps { get; init; }
}

public sealed class GatewayJourneyStepStatus
{
    public required GatewayJourneyStep Step { get; init; }
    public GatewayModule? Module { get; init; }
    public required bool HasModule { get; init; }
    public required bool IsAuthorized { get; init; }
    public bool CanOpenPublic { get; init; }
    public string? PublicHref { get; init; }
}

public static class GatewayJourneyPublicPaths
{
    private static readonly HashSet<string> Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "/careers",
        "/careers/track"
    };

    public static bool TryResolve(string? candidate, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var normalized = candidate.Trim();
        if (!normalized.StartsWith('/'))
            return false;
        if (normalized.StartsWith("//", StringComparison.Ordinal))
            return false;
        if (normalized.Contains("://", StringComparison.Ordinal))
            return false;

        if (!Allowlist.Contains(normalized))
            return false;

        path = normalized;
        return true;
    }
}
