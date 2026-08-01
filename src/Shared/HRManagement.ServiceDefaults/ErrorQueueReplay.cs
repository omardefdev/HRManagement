using MassTransit;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// INT-002: controlled replay helper — rejects cross-tenant republish from error payloads.
/// </summary>
public static class ErrorQueueReplay
{
    public const string TenantHeader = TenantIdMiddleware.HeaderName;

    public static void EnsureTenantMatch(Guid expectedTenantId, Headers headers)
    {
        string? header = null;
        if (headers.TryGetHeader(TenantHeader, out var raw) && raw is not null)
            header = raw.ToString();
        header ??= headers.Get<string>(TenantHeader);

        if (!Guid.TryParse(header, out var actual) || actual != expectedTenantId)
            throw new InvalidOperationException(
                "رفض إعادة التشغيل عبر المستأجرين — رأس المستأجر غير مطابق (INT-002).");
    }

    public static async Task ReplayAsync<T>(
        IPublishEndpoint publishEndpoint,
        T message,
        Guid tenantId,
        Headers? originalHeaders,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (originalHeaders is not null)
            EnsureTenantMatch(tenantId, originalHeaders);

        await publishEndpoint.Publish(message, ctx =>
        {
            ctx.Headers.Set(TenantHeader, tenantId.ToString("D"));
            if (originalHeaders is not null)
            {
                var correlation = originalHeaders.Get<string>(CorrelationIdMiddleware.HeaderName);
                if (!string.IsNullOrEmpty(correlation))
                    ctx.Headers.Set(CorrelationIdMiddleware.HeaderName, correlation);
            }
        }, cancellationToken);

        logger?.LogInformation("Replayed {MessageType} for tenant {TenantId}", typeof(T).Name, tenantId);
    }
}
