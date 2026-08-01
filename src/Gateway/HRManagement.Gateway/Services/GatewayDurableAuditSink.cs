using HRManagement.Gateway.Models;
using HRManagement.ServiceDefaults;

namespace HRManagement.Gateway.Services;

/// <summary>Bridges module-level sensitive audit into the durable gateway audit queue.</summary>
public sealed class GatewayDurableAuditSink(IGatewayAuditQueue queue) : IDurableAuditSink
{
    public bool TryPersist(DurableAuditEvent auditEvent)
    {
        var summary = auditEvent.ResourceSummary ?? string.Empty;
        if (summary.Length > 512)
            summary = summary[..512];

        return queue.TryEnqueue(new GatewayAuditEntry
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = auditEvent.CorrelationId ?? string.Empty,
            UserSub = auditEvent.UserSub,
            UserName = auditEvent.UserName,
            Method = "AUDIT",
            Path = $"/{auditEvent.Domain}/{auditEvent.Action}",
            QueryString = summary,
            StatusCode = 0,
            ElapsedMs = 0,
            Decision = "sensitive_access",
            ClientIp = null,
            UserAgent = null
        });
    }
}
