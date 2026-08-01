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
}
