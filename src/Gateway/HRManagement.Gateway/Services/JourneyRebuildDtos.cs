namespace HRManagement.Gateway.Services;

public sealed class JourneyRebuildResult
{
    public required string CorrelationId { get; init; }
    public string? User { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? ScopeOrgUnitId { get; init; }
    public IReadOnlyList<JourneyDecisionDto> Decisions { get; init; } = [];
    public IReadOnlyList<JourneyHttpEventDto> HttpEvents { get; init; } = [];
    public IReadOnlyList<JourneyPolicyEventDto> PolicyEvents { get; init; } = [];
    public IReadOnlyList<JourneySecurityEventDto> SecurityEvents { get; init; } = [];
    public IReadOnlyList<string> Gaps { get; init; } = [];
}

public sealed class JourneyDecisionDto
{
    public required string Source { get; init; }
    public required string Decision { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset? At { get; init; }
}

public sealed class JourneyHttpEventDto
{
    public long Id { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public int StatusCode { get; init; }
    public required string Decision { get; init; }
    public string? Reason { get; init; }
}

public sealed class JourneyPolicyEventDto
{
    public Guid Id { get; init; }
    public required string Domain { get; init; }
    public required string Action { get; init; }
    public string? DecisionJson { get; init; }
    public DateTime OccurredAt { get; init; }
}

public sealed class JourneySecurityEventDto
{
    public long Id { get; init; }
    public required string EventType { get; init; }
    public string? Resource { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
}

public interface IJourneyRebuildService
{
    Task<JourneyRebuildResult> RebuildAsync(string correlationId, string? authorizationHeader, CancellationToken ct = default);
}
