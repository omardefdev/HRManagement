using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HRManagement.ServiceDefaults;

/// <summary>Rejects direct module access in production when gateway proof is required.</summary>
public sealed class GatewayCallerProofMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IConfiguration configuration, IHostEnvironment env)
    {
        var behindGateway = configuration.GetValue("HrAuth:BehindGateway", true);
        var enforce = configuration.GetValue("GatewayCallerProof:Enforce", !env.IsDevelopment()) && behindGateway;

        if (enforce && !IsExemptPath(context.Request.Path))
        {
            if (!GatewayCallerProof.Validate(context, configuration, enforce: true))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Direct module access is not permitted.");
                return;
            }
        }

        await next(context);
    }

    private static bool IsExemptPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return value.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/alive", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/ready", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase);
    }
}

public static class GatewayCallerProofMiddlewareExtensions
{
    public static IApplicationBuilder UseHrGatewayCallerProof(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayCallerProofMiddleware>();
}
