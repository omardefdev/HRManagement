using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults;
// SecurityAuditEvent in Models

namespace HRManagement.Gateway.Middleware;

public sealed class GatewayAuditMiddleware(RequestDelegate next)
{
    private static readonly Regex SensitiveQuery = new(
        "(token|access_token|id_token|refresh_token|client_secret|password)=([^&]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task InvokeAsync(
        HttpContext context,
        IGatewayAuditQueue auditQueue,
        ISecurityAuditPublisher securityAudit,
        ILogger<GatewayAuditMiddleware> logger)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var shouldAudit = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase);
        if (!shouldAudit)
        {
            await next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        await next(context);
        sw.Stop();

        var correlationId = context.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
            ?? context.TraceIdentifier;
        var statusCode = context.Response.StatusCode;
        var decision = statusCode switch
        {
            401 => "challenge",
            403 => "forbidden",
            >= 400 and < 500 => "client_error",
            >= 500 => "server_error",
            _ => "allowed"
        };
        var rawQuery = context.Request.QueryString.Value ?? string.Empty;
        var redacted = SensitiveQuery.Replace(rawQuery, "$1=REDACTED");
        if (redacted.Length > 512)
            redacted = redacted[..512];

        var sub = context.User.FindFirstValue("sub");
        var userName = context.User.Identity?.Name
            ?? context.User.FindFirstValue(ClaimTypes.Name)
            ?? context.User.FindFirstValue("preferred_username");

        var denial = OrgScopeDenialContext.Get(context);
        Guid? tenantId = denial?.TenantId;
        if (!tenantId.HasValue)
        {
            var tenantClaim = context.User.FindFirstValue(TenantContext.TenantClaimType);
            if (Guid.TryParse(tenantClaim, out var parsedTenant))
                tenantId = parsedTenant;
        }

        Guid? scopeOrgUnitId = denial?.ScopeOrgUnitId;
        if (!scopeOrgUnitId.HasValue)
        {
            var scopeClaim = context.User.FindFirstValue(HrOrgScopeVisibility.ScopeOrgUnitClaimType);
            if (Guid.TryParse(scopeClaim, out var parsedScope))
                scopeOrgUnitId = parsedScope;
        }

        var entry = new GatewayAuditEntry
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            UserSub = sub,
            UserName = userName,
            Method = context.Request.Method,
            Path = path.Length > 512 ? path[..512] : path,
            QueryString = redacted,
            StatusCode = statusCode,
            ElapsedMs = sw.ElapsedMilliseconds,
            Decision = decision,
            ClientIp = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = (context.Request.Headers.UserAgent.ToString() ?? string.Empty) switch
            {
                var ua when ua.Length > 512 => ua[..512],
                var ua => ua
            },
            TenantId = tenantId,
            ScopeOrgUnitId = scopeOrgUnitId,
            Reason = denial?.Reason,
            ResourceType = denial?.ResourceType,
            ResourceId = denial?.ResourceId
        };

        if (!auditQueue.TryEnqueue(entry))
            logger.LogWarning("Gateway audit queue is full; dropping audit event for {Path}", path);

        // OBS-003: bulk lookup / bulk export SIEM signals
        if (path.Contains("/lookup", StringComparison.OrdinalIgnoreCase) && statusCode is >= 200 and < 300)
            securityAudit.RecordLookupHit(sub, correlationId);

        if (IsBulkExportPath(path) && statusCode is >= 200 and < 300)
        {
            _ = securityAudit.PublishAsync(new SecurityAuditEvent
            {
                EventType = SecurityAuditEventTypes.BulkExport,
                ActorSub = sub,
                CorrelationId = correlationId,
                Resource = path.Length > 512 ? path[..512] : path,
                DetailJson = $"{{\"status\":{statusCode},\"method\":\"{entry.Method}\"}}",
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
        }

        if (decision == "forbidden" && denial is not null)
        {
            _ = securityAudit.PublishAsync(new SecurityAuditEvent
            {
                EventType = SecurityAuditEventTypes.OrgScopeDenied,
                ActorSub = sub,
                CorrelationId = correlationId,
                TenantId = denial.TenantId,
                Resource = denial.ResourceId ?? path,
                DetailJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    reason = denial.Reason,
                    scopeOrgUnitId = denial.ScopeOrgUnitId,
                    resourceType = denial.ResourceType,
                    resourceId = denial.ResourceId,
                    statusCode,
                    method = entry.Method
                }),
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
        }

        if (decision == "forbidden" && IsPrivilegedPath(path))
        {
            _ = securityAudit.PublishAsync(new SecurityAuditEvent
            {
                EventType = SecurityAuditEventTypes.PrivilegedGateway,
                ActorSub = sub,
                CorrelationId = correlationId,
                Resource = path.Length > 512 ? path[..512] : path,
                DetailJson = $"{{\"status\":{statusCode},\"method\":\"{entry.Method}\"}}",
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
        }

        logger.LogInformation(
            "GatewayAudit {Decision} {Method} {Path} => {StatusCode} in {ElapsedMs}ms (corr={CorrelationId})",
            decision, entry.Method, entry.Path, statusCode, entry.ElapsedMs, correlationId);
    }

    private static bool IsPrivilegedPath(string path) =>
        path.Contains("/payroll", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/auth-directory", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/files", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/platform/audit", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/admin", StringComparison.OrdinalIgnoreCase);

    private static bool IsBulkExportPath(string path) =>
        path.Contains("/Export", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/BulkPayslips", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/Reports/", StringComparison.OrdinalIgnoreCase);
}

public static class GatewayAuditMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayAudit(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayAuditMiddleware>();
}
