namespace HRManagement.Gateway.Models;

public sealed class GatewayDashboardViewModel
{
    public required IReadOnlyList<GatewayModuleStatus> Modules { get; init; }
    public required GatewayHealthSummary Summary { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public int ActiveUsersCount { get; init; }
    public int PendingApprovalsCount { get; init; }
    public List<ModuleRequestStat> ModuleStats { get; init; } = [];
}

public sealed class ModuleRequestStat
{
    public required string ModuleName { get; init; }
    public long RequestCount { get; init; }
    public long ErrorCount { get; init; }
    public double AvgLatencyMs { get; init; }
}

public sealed class GatewayModuleStatus
{
    public required GatewayModule Module { get; init; }
    public required bool IsAuthorized { get; init; }
    public required bool IsHealthy { get; init; }
    public required int StatusCode { get; init; }
    public required long LatencyMs { get; init; }
    public string? Error { get; init; }
}

public sealed class GatewayHealthSummary
{
    public required int Total { get; init; }
    public required int Healthy { get; init; }
    public required int Degraded { get; init; }
    public required int Unauthorized { get; init; }
}
