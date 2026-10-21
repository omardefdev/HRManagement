namespace HRManagement.Gateway.Models;

public sealed class GatewayAuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    /// <summary>Monotonic process-local sequence for gap detection after persistence.</summary>
    public long SequenceNumber { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? UserSub { get; set; }
    public string? UserName { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string QueryString { get; set; } = "";
    public int StatusCode { get; set; }
    public long ElapsedMs { get; set; }
    public string Decision { get; set; } = "";
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>JWT tenant_id at decision time (AC-03).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Caller scope_org_unit_id when scoped (AC-03).</summary>
    public Guid? ScopeOrgUnitId { get; set; }

    /// <summary>Denial reason e.g. org-scope-denied (AC-03).</summary>
    public string? Reason { get; set; }

    /// <summary>Resource type e.g. Employee, OrgUnit (AC-03).</summary>
    public string? ResourceType { get; set; }

    /// <summary>Resource identifier (AC-03).</summary>
    public string? ResourceId { get; set; }

    /// <summary>OBS-003 hash-chain key (single active chain = "default").</summary>
    public string ChainKey { get; set; } = "default";

    /// <summary>OBS-003 previous event hash (or GENESIS).</summary>
    public string PrevHash { get; set; } = "";

    /// <summary>OBS-003 SHA-256 of canonical fields + PrevHash.</summary>
    public string EventHash { get; set; } = "";
}
