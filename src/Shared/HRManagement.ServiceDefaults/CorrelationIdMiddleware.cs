using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace HRManagement.ServiceDefaults;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    public async Task Invoke(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

        context.Response.Headers.Append(HeaderName, correlationId);
        context.Items[ItemKey] = correlationId;
        CorrelationIdFlow.Set(correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
            await next(context);
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseHrCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}
