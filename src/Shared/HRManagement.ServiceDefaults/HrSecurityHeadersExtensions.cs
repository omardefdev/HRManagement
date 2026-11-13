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

    public const string CspHeader = "Content-Security-Policy";
    public const string CspReportOnlyHeader = "Content-Security-Policy-Report-Only";

    /// <summary>
    /// The security headers of every response. PLAN-67 (D-67-1, G-UX-01): they are written when the response starts, and each only if
    /// the response does not carry it already. A page the gateway forwards keeps the policy and the nonce of the service that rendered it
    /// (YARP copies the destination's headers before the response starts, and adds to headers already present instead of replacing them,
    /// so the gateway's policy written before forwarding made two policies, and the browser blocked every inline block that had to satisfy
    /// both); a page the gateway renders, or a response that arrives without them, gets the gateway's. The policy itself is unchanged.
    /// </summary>
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

            var headerName = string.Equals(opts.CspMode, "Enforce", StringComparison.OrdinalIgnoreCase)
                ? CspHeader
                : CspReportOnlyHeader;
            var csp = BuildContentSecurityPolicy(opts, nonce);

            context.Response.OnStarting(static state =>
            {
                var (response, name, policy) = ((HttpResponse, string, string))state;
                ApplySecurityHeaders(response.Headers, name, policy);
                return Task.CompletedTask;
            }, (context.Response, headerName, csp));

            await next();
        });

        return app;
    }

    /// <summary>The policy for one response: scripts and styles from the app's origin or carrying the response's nonce, and nothing inline.</summary>
    public static string BuildContentSecurityPolicy(HrSecurityHeadersOptions opts, string nonce)
    {
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

        return csp.ToString();
    }

    /// <summary>
    /// Writes each security header that <paramref name="headers"/> does not carry yet (D-67-1). A response that already has a policy, in
    /// either mode, keeps it alone.
    /// </summary>
    public static void ApplySecurityHeaders(IHeaderDictionary headers, string cspHeaderName, string csp)
    {
        SetIfAbsent(headers, "X-Content-Type-Options", "nosniff");
        SetIfAbsent(headers, "X-Frame-Options", "SAMEORIGIN");
        SetIfAbsent(headers, "Referrer-Policy", "strict-origin-when-cross-origin");
        SetIfAbsent(headers, "X-XSS-Protection", "0");
        SetIfAbsent(headers, "Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");

        if (!headers.ContainsKey(CspHeader) && !headers.ContainsKey(CspReportOnlyHeader))
            headers[cspHeaderName] = csp;
    }

    private static void SetIfAbsent(IHeaderDictionary headers, string name, string value)
    {
        if (!headers.ContainsKey(name))
            headers[name] = value;
    }

    public static string? GetCspNonce(this HttpContext httpContext) =>
        httpContext.Items.TryGetValue(NonceItemKey, out var v) ? v as string : null;
}
