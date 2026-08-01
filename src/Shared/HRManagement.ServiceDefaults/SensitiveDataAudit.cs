using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>سجل تدقيق موجّه للموارد الحساسة (صحة، شكاوى، إلخ) عبر Serilog + مسار دائم.</summary>
public static class SensitiveDataAudit
{
    public static void Log(
        HttpContext? httpContext,
        string domain,
        string action,
        string resourceSummary)
    {
        var userName = httpContext?.User?.Identity?.Name
            ?? httpContext?.User?.FindFirst("preferred_username")?.Value
            ?? httpContext?.User?.FindFirst("sub")?.Value
            ?? "anonymous";
        var userSub = httpContext?.User?.FindFirst("sub")?.Value;
        var correlationId = httpContext?.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
            ?? httpContext?.TraceIdentifier;

        global::Serilog.Log.ForContext("AuditSensitive", true)
            .ForContext("AuditDomain", domain)
            .Information(
                "SensitiveAudit {Domain} {Action} {Resource} User={User}",
                domain,
                action,
                resourceSummary,
                userName);

        var auditEvent = new DurableAuditEvent
        {
            Domain = domain,
            Action = action,
            ResourceSummary = resourceSummary,
            UserSub = userSub,
            UserName = userName,
            CorrelationId = correlationId
        };

        var sink = httpContext?.RequestServices?.GetService<IDurableAuditSink>();
        if (sink is not null)
        {
            sink.TryPersist(auditEvent);
            return;
        }

        // Fallback when no DI sink is registered (tests / background): durable JSONL file.
        new FileDurableAuditSink().TryPersist(auditEvent);
    }
}
