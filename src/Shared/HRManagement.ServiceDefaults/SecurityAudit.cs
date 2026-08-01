using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>OBS-003 typed security audit events (modules → JSONL / optional gateway sink).</summary>
public static class SecurityAudit
{
    public static class EventTypes
    {
        public const string BulkExport = "BulkExport";
        public const string BulkLookup = "BulkLookup";
        public const string FileAccessSensitive = "FileAccessSensitive";
        public const string RoleChange = "RoleChange";
        public const string PayrollOverride = "PayrollOverride";
        public const string PrivilegedGateway = "PrivilegedGateway";
        public const string AuditPurge = "AuditPurge";
    }

    public static void Log(
        HttpContext? httpContext,
        string eventType,
        string? resource = null,
        string? detailJson = null)
    {
        var userSub = httpContext?.User?.FindFirst("sub")?.Value;
        var correlationId = httpContext?.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
            ?? httpContext?.TraceIdentifier;

        global::Serilog.Log.ForContext("SecurityAudit", true)
            .ForContext("SecurityEventType", eventType)
            .Information(
                "SecurityAudit {EventType} {Resource} Actor={Actor}",
                eventType,
                resource,
                userSub ?? "anonymous");

        var payload = new SecurityAuditPayload
        {
            EventType = eventType,
            ActorSub = userSub,
            CorrelationId = correlationId,
            Resource = resource,
            DetailJson = detailJson,
            OccurredAtUtc = DateTimeOffset.UtcNow
        };

        var sink = httpContext?.RequestServices?.GetService<ISecurityAuditSink>();
        if (sink is not null)
        {
            sink.TryPublish(payload);
            return;
        }

        new FileSecurityAuditSink().TryPublish(payload);
    }
}

public interface ISecurityAuditSink
{
    bool TryPublish(SecurityAuditPayload payload);
}

public sealed class SecurityAuditPayload
{
    public required string EventType { get; init; }
    public string? ActorSub { get; init; }
    public string? CorrelationId { get; init; }
    public string? Resource { get; init; }
    public string? DetailJson { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public Guid? TenantId { get; init; }
}

public sealed class FileSecurityAuditSink : ISecurityAuditSink
{
    private readonly string _directory;

    public FileSecurityAuditSink(string? directory = null)
    {
        _directory = directory
            ?? Environment.GetEnvironmentVariable("HR_SECURITY_AUDIT_PATH")
            ?? Path.Combine(Path.GetTempPath(), "hrm-security-audit");
        Directory.CreateDirectory(_directory);
    }

    public bool TryPublish(SecurityAuditPayload payload)
    {
        var path = Path.Combine(_directory, $"security-{DateTime.UtcNow:yyyyMMdd}.jsonl");
        var line = System.Text.Json.JsonSerializer.Serialize(payload);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(line);
        return true;
    }
}
