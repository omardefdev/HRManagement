namespace HRManagement.Gateway.Models;

public sealed class GatewayAuditSearchRequest
{
    public int? StatusCode { get; init; }
    public string? CorrelationId { get; init; }
    /// <summary>PERF-002: 1h, 24h (default), 7d. "all" is rejected.</summary>
    public string? TimeRange { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
    /// <summary>PERF-002: keyset cursor; when set, Page is ignored.</summary>
    public string? Cursor { get; init; }
}

public sealed class GatewayAuditSearchResponse
{
    public required IReadOnlyList<GatewayAuditRowViewModel> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int Count401 { get; init; }
    public required int Count403 { get; init; }
    public required int Count502 { get; init; }
    public string? NextCursor { get; init; }
    public bool HasMore { get; init; }
    public string? Error { get; init; }
}

public sealed class GatewayAuditRowViewModel
{
    public long Id { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string Decision { get; init; }
    public required int StatusCode { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public string? UserName { get; init; }
    public required string CorrelationId { get; init; }
    public required long ElapsedMs { get; init; }
}
