using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Core.Infrastructure.Middleware;

/// <summary>Which <c>careers-otp</c> window an endpoint uses (HCM-030 H3).</summary>
public enum CareersOtpOperation
{
    Request,
    Verify
}

/// <summary>
/// HCM-030 (PLAN-30 H3): marks a <c>[EnableRateLimiting("careers-otp")]</c> endpoint as code request or code check
/// (separate per-client windows). Without it the endpoint counts as <see cref="CareersOtpOperation.Request"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CareersOtpRateLimitAttribute(CareersOtpOperation operation) : Attribute
{
    public CareersOtpOperation Operation { get; } = operation;
}

/// <summary>
/// HCM-006: rate limit public careers apply/track endpoints. HCM-030 (H3): <c>careers-otp</c> adds a per-client window.
/// <para>
/// ASP.NET Core applies one endpoint policy per endpoint, so the limits are enforced by one chained global limiter
/// (<see cref="PartitionedRateLimiter.CreateChained{TResource}"/>): (1) the shared <c>public-careers</c> window
/// (<c>Recruitment:PublicCareersRateLimitPerMinute</c>, all visitors together, as before) for every endpoint that names
/// <c>public-careers</c> or <c>careers-otp</c>; (2) a per-client 10-minute window for <c>careers-otp</c> endpoints
/// (<c>Recruitment:Portal:OtpRequestsPerIpPer10Min</c> 5 / <c>…OtpVerifyPerIpPer10Min</c> 20), keyed by
/// <see cref="CareersClientIpResolver"/>. The named policies only select the windows (no limit of their own). Every other
/// endpoint is not limited. The database counters per application (resend window, attempts, lock) remain the real guard.
/// </para>
/// </summary>
public static class PublicCareersRateLimitExtensions
{
    public const string PolicyName = "public-careers";

    /// <summary>HCM-030 (H3): OTP request/verify endpoints — the shared window plus the per-client window.</summary>
    public const string OtpPolicyName = "careers-otp";

    public static IServiceCollection AddPublicCareersRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var globalLimiter = CreateGlobalLimiter(configuration, CareersClientIpResolver.FromConfiguration(configuration));
        services.AddRateLimiter(options =>
        {
            // Markers: the endpoint metadata names the policy, the chained global limiter applies the windows.
            options.AddPolicy(PolicyName, _ => RateLimitPartition.GetNoLimiter(PolicyName));
            options.AddPolicy(OtpPolicyName, _ => RateLimitPartition.GetNoLimiter(OtpPolicyName));
            options.GlobalLimiter = globalLimiter;
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, ct) => WriteRejectionPageAsync(context.HttpContext, context.Lease, ct);
        });
        return services;
    }

    public static IApplicationBuilder UsePublicCareersRateLimiting(this IApplicationBuilder app) =>
        app.UseRateLimiter();

    /// <summary>The chained limiter (shared public window → per-client OTP window).</summary>
    public static PartitionedRateLimiter<HttpContext> CreateGlobalLimiter(
        IConfiguration configuration, CareersClientIpResolver clientIp)
    {
        var permitLimit = configuration.GetValue("Recruitment:PublicCareersRateLimitPerMinute", 30);
        var requestsPerClient = Math.Max(1, configuration.GetValue("Recruitment:Portal:OtpRequestsPerIpPer10Min", 5));
        var verifyPerClient = Math.Max(1, configuration.GetValue("Recruitment:Portal:OtpVerifyPerIpPer10Min", 20));

        var shared = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            PolicyOf(context) is PolicyName or OtpPolicyName
                ? RateLimitPartition.GetFixedWindowLimiter(PolicyName, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                })
                : RateLimitPartition.GetNoLimiter(string.Empty));

        var perClient = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (PolicyOf(context) != OtpPolicyName)
                return RateLimitPartition.GetNoLimiter(string.Empty);
            var operation = OperationOf(context);
            return RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(operation, clientIp.ResolveKey(context)),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = operation == CareersOtpOperation.Verify ? verifyPerClient : requestsPerClient,
                    Window = TimeSpan.FromMinutes(10),
                    QueueLimit = 0
                });
        });

        return PartitionedRateLimiter.CreateChained(shared, perClient);
    }

    /// <summary>Per-client partition: <c>otp-request|203.0.113.7</c> / <c>otp-verify|…</c>.</summary>
    public static string PartitionKey(CareersOtpOperation operation, string clientKey) =>
        (operation == CareersOtpOperation.Verify ? "otp-verify|" : "otp-request|") + clientKey;

    private static string? PolicyOf(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

    private static CareersOtpOperation OperationOf(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<CareersOtpRateLimitAttribute>()?.Operation ?? CareersOtpOperation.Request;

    /// <summary>Arabic 429 page (no script, no inline style: it is served under the CSP of the portal).</summary>
    private static async ValueTask WriteRejectionPageAsync(HttpContext http, RateLimitLease lease, CancellationToken ct)
    {
        if (http.Response.HasStarted)
            return;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        http.Response.ContentType = "text/html; charset=utf-8";
        var home = http.Request.PathBase.HasValue ? http.Request.PathBase.Value + "/" : "/";
        var html = new StringBuilder()
            .Append("<!DOCTYPE html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\" />")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />")
            .Append("<title>طلبات كثيرة — بوابة التوظيف الحكومية</title></head><body>")
            .Append("<main role=\"main\"><h1>عدد كبير من الطلبات</h1>")
            .Append("<p>تجاوزت عدد المحاولات المسموح به خلال فترة قصيرة. يرجى الانتظار قليلاً ثم المحاولة مرة أخرى.</p>")
            .Append("<p><a href=\"").Append(System.Net.WebUtility.HtmlEncode(home)).Append("\">العودة إلى بوابة التوظيف</a></p>")
            .Append("</main></body></html>")
            .ToString();
        await http.Response.WriteAsync(html, Encoding.UTF8, ct);
    }
}
