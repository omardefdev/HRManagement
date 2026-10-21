namespace HRManagement.Gateway.Models;

/// <summary>OBS-003 typed security events for SIEM rules.</summary>
public sealed class SecurityAuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string EventType { get; set; } = "";
    public string? ActorSub { get; set; }
    public string? CorrelationId { get; set; }
    public string? Resource { get; set; }
    public string? DetailJson { get; set; }
    public Guid? TenantId { get; set; }
}

public static class SecurityAuditEventTypes
{
    public const string BulkLookup = "BulkLookup";
    public const string BulkExport = "BulkExport";
    public const string FileAccessSensitive = "FileAccessSensitive";
    public const string RoleChange = "RoleChange";
    public const string PayrollOverride = "PayrollOverride";
    public const string PrivilegedGateway = "PrivilegedGateway";
    public const string AuditPurge = "AuditPurge";
    public const string OrgScopeDenied = "OrgScopeDenied";
    public const string PlatformEnterTenant = "PlatformEnterTenant";
    public const string PlatformExitTenant = "PlatformExitTenant";
}

/// <summary>OBS-003 legal hold preventing retention purge.</summary>
public sealed class AuditLegalHold
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset? RangeStartUtc { get; set; }
    public DateTimeOffset? RangeEndUtc { get; set; }
    public string? CorrelationId { get; set; }
    public string? UserSub { get; set; }
    public bool Active { get; set; } = true;
}
