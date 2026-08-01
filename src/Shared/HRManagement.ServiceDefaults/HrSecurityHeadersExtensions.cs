using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public sealed class HrSecurityHeadersOptions
{
    public const string SectionName = "Security";

    /// <summary>ReportOnly (default) or Enforce.</summary>
    public string CspMode { get; set; } = "ReportOnly";

    /// <summary>Extra connect-src hosts (e.g. Keycloak authority), space-separated or as array in config.</summary>
    public string[]? CspConnectSrcExtra { get; set; }

    public string? CspReportUri { get; set; }

    public bool EnableSecurityHeaders { get; set; } = true;
}

public static class HrSecurityHeadersExtensions
{
    public const string NonceItemKey = "HrCspNonce";

    public static IServiceCollection AddHrSecurityHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.Configure<HrSecurityHeadersOptions>(configuration.GetSection(HrSecurityHeadersOptions.SectionName));
        return services;
    }

    public static WebApplication UseHrSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var opts = context.RequestServices.GetService<Microsoft.Extensions.Options.IOptions<HrSecurityHeadersOptions>>()?.Value
                       ?? new HrSecurityHeadersOptions();

            if (!opts.EnableSecurityHeaders)
            {
                await next();
                return;
            }

            var nonceBytes = RandomNumberGenerator.GetBytes(16);
            var nonce = Convert.ToBase64String(nonceBytes);
            context.Items[NonceItemKey] = nonce;

            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["X-XSS-Protection"] = "0";
            context.Response.Headers["Permissions-Policy"] =
                "camera=(), microphone=(), geolocation=(), payment=()";

            var connectExtra = opts.CspConnectSrcExtra is { Length: > 0 }
                ? " " + string.Join(' ', opts.CspConnectSrcExtra)
                : string.Empty;

            var csp = new StringBuilder();
            csp.Append("default-src 'self'; ");
            csp.Append($"script-src 'self' 'nonce-{nonce}'; ");
            csp.Append($"style-src 'self' 'nonce-{nonce}'; ");
            csp.Append("img-src 'self' data: blob:; ");
            csp.Append("font-src 'self' data:; ");
            csp.Append($"connect-src 'self'{connectExtra}; ");
            csp.Append("frame-ancestors 'self'; ");
            csp.Append("base-uri 'self'; ");
            csp.Append("object-src 'none'; ");
            csp.Append("form-action 'self'");

            if (!string.IsNullOrWhiteSpace(opts.CspReportUri))
                csp.Append($"; report-uri {opts.CspReportUri}");

            var headerName = string.Equals(opts.CspMode, "Enforce", StringComparison.OrdinalIgnoreCase)
                ? "Content-Security-Policy"
                : "Content-Security-Policy-Report-Only";

            context.Response.Headers[headerName] = csp.ToString();

            await next();
        });

        return app;
    }

    public static string? GetCspNonce(this HttpContext httpContext) =>
        httpContext.Items.TryGetValue(NonceItemKey, out var v) ? v as string : null;
}
