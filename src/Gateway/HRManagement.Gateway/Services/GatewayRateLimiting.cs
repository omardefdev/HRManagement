using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace HRManagement.Gateway.Services;

/// <summary>
/// SEC-002: the gateway's request limits. A global fixed one-minute window per client IP, and the <c>gateway-ui</c>
/// policy for per-route use (<c>.RequireRateLimiting("gateway-ui")</c>), one window that all callers share. Both allow
/// <c>RateLimiting:PermitLimitPerMinute</c> requests (default 120, clamped to 30..2000).
/// <para>
/// No queue (PLAN-35 final report, defect 15): a request over the limit is answered 429 at once with <c>Retry-After</c>.
/// With the old queue of 20 it waited for the next window, up to a minute (41 s in the CI E2E stack, past Playwright's
/// 30 s timeout), and so did every client behind the same address. The client IP is <c>RemoteIpAddress</c>, which
/// <c>UseForwardedHeaders</c> rewrites only for trusted proxies (<see cref="HRManagement.ServiceDefaults.ForwardedProxyTrust"/>).
/// </para>
/// </summary>
public static class GatewayRateLimiting
{
    public const string UiPolicyName = "gateway-ui";
    public const string PermitLimitKey = "RateLimiting:PermitLimitPerMinute";
    public const int DefaultPermitLimit = 120;

    /// <summary>The 429 text of the gateway status page (<c>Views/Home/Status.cshtml</c>).</summary>
    public const string RejectionText = "عدد الطلبات كبير. حاول بعد قليل.";

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static int PermitLimit(IConfiguration configuration) =>
        Math.Clamp(configuration.GetValue(PermitLimitKey, DefaultPermitLimit), 30, 2000);

    public static void Configure(RateLimiterOptions options, int permitLimit)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => WindowOptions(permitLimit)));
        options.OnRejected = (context, ct) => RejectAsync(context, "global", "ip", permitLimit, ct);
        options.AddPolicy(UiPolicyName, new UiPolicy(permitLimit));
    }

    private static FixedWindowRateLimiterOptions WindowOptions(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = Window,
        QueueLimit = 0
    };

    /// <summary>
    /// 429 with <c>Retry-After</c> and one warning that names the partition kind, not the address or the user. The body
    /// matters: <c>UseStatusCodePagesWithReExecute</c> runs before the limiter, and would re-run an empty 429 as
    /// <c>/Home/ErrorStatus/429</c> through the limiter again.
    /// </summary>
    private static async ValueTask RejectAsync(
        OnRejectedContext context, string limiter, string partitionKind, int permitLimit, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        // A fixed window reports the whole window, not the time left in it, so this is an upper bound.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter) ? leaseRetryAfter : Window;
        var retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
        // Path.Value only: Request.Method and Request.Path are cs/log-forging sources for the CodeQL gate.
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GatewayRateLimiting)).LogWarning(
            "Gateway rate limit: {Path} answered 429 by the {Limiter} limiter ({PartitionKind} partition, {PermitLimit} per minute), retry after {RetryAfterSeconds} s",
            http.Request.Path.Value,
            limiter,
            partitionKind,
            permitLimit,
            retryAfterSeconds);

        if (http.Response.HasStarted)
            return;
        http.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        http.Response.ContentType = "text/plain; charset=utf-8";
        await http.Response.WriteAsync(RejectionText, cancellationToken);
    }

    /// <summary><c>gateway-ui</c>: the same window under one partition key, with its own rejection log line.</summary>
    private sealed class UiPolicy : IRateLimiterPolicy<string>
    {
        private readonly FixedWindowRateLimiterOptions _window;

        public UiPolicy(int permitLimit)
        {
            _window = WindowOptions(permitLimit);
            OnRejected = (context, ct) => RejectAsync(context, UiPolicyName, "shared", permitLimit, ct);
        }

        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; }

        public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
            RateLimitPartition.GetFixedWindowLimiter(UiPolicyName, _ => _window);
    }
}
