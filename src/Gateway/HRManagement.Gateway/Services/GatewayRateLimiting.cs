using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace HRManagement.Gateway.Services;

/// <summary>The gateway's per-minute limits (each clamped to 30..2000) and whether static assets are exempt.</summary>
public sealed record GatewayRateLimitSettings(int AnonymousPermitLimit, int AuthenticatedPermitLimit, bool ExemptStaticAssets);

/// <summary>
/// SEC-002: the gateway's request limits. A global fixed one-minute window partitioned by
/// <see cref="GatewayRateLimitPartitioner"/> (PLAN-36): per user for signed-in callers
/// (<c>RateLimiting:AuthenticatedPermitLimitPerMinute</c>, default 600), per client IP for anonymous ones
/// (<c>RateLimiting:PermitLimitPerMinute</c>, default 120), and no limit for static assets
/// (<c>RateLimiting:ExemptStaticAssets</c>, default true). The <c>gateway-ui</c> policy, for per-route use
/// (<c>.RequireRateLimiting("gateway-ui")</c>), is one window that all callers share at the anonymous limit.
/// <para>
/// No queue (PLAN-35 final report, defect 15): a request over the limit is answered 429 at once with <c>Retry-After</c>.
/// With the old queue of 20 it waited for the next window, up to a minute (41 s in the CI E2E stack, past Playwright's
/// 30 s timeout), and so did every client behind the same address.
/// </para>
/// </summary>
public static class GatewayRateLimiting
{
    public const string UiPolicyName = "gateway-ui";
    public const string PermitLimitKey = "RateLimiting:PermitLimitPerMinute";
    public const string AuthenticatedPermitLimitKey = "RateLimiting:AuthenticatedPermitLimitPerMinute";
    public const string ExemptStaticAssetsKey = "RateLimiting:ExemptStaticAssets";
    public const int DefaultPermitLimit = 120;
    public const int DefaultAuthenticatedPermitLimit = 600;
    public const int MinimumPermitLimit = 30;
    public const int MaximumPermitLimit = 2000;

    /// <summary>The 429 text of the gateway status page (<c>Views/Home/Status.cshtml</c>).</summary>
    public const string RejectionText = "عدد الطلبات كبير. حاول بعد قليل.";

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>The anonymous (per-IP) limit.</summary>
    public static int PermitLimit(IConfiguration configuration) =>
        Math.Clamp(configuration.GetValue(PermitLimitKey, DefaultPermitLimit), MinimumPermitLimit, MaximumPermitLimit);

    public static int AuthenticatedPermitLimit(IConfiguration configuration) =>
        Math.Clamp(
            configuration.GetValue(AuthenticatedPermitLimitKey, DefaultAuthenticatedPermitLimit),
            MinimumPermitLimit,
            MaximumPermitLimit);

    public static GatewayRateLimitSettings Settings(IConfiguration configuration) =>
        new(PermitLimit(configuration), AuthenticatedPermitLimit(configuration), configuration.GetValue(ExemptStaticAssetsKey, true));

    /// <summary>One limit for anonymous and signed-in callers alike, static assets exempt.</summary>
    public static void Configure(RateLimiterOptions options, int permitLimit) =>
        Configure(options, new GatewayRateLimitSettings(permitLimit, permitLimit, ExemptStaticAssets: true));

    public static void Configure(RateLimiterOptions options, GatewayRateLimitSettings settings)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var partition = GatewayRateLimitPartitioner.Resolve(httpContext, settings.ExemptStaticAssets);
            return partition.Kind switch
            {
                GatewayRateLimitPartitionKind.Exempt => RateLimitPartition.GetNoLimiter(partition.Key),
                GatewayRateLimitPartitionKind.User => RateLimitPartition.GetFixedWindowLimiter(
                    partition.Key, _ => WindowOptions(settings.AuthenticatedPermitLimit)),
                _ => RateLimitPartition.GetFixedWindowLimiter(
                    partition.Key, _ => WindowOptions(settings.AnonymousPermitLimit))
            };
        });
        options.OnRejected = (context, ct) =>
        {
            var partition = GatewayRateLimitPartitioner.Resolve(context.HttpContext, settings.ExemptStaticAssets);
            var limit = partition.Kind == GatewayRateLimitPartitionKind.User
                ? settings.AuthenticatedPermitLimit
                : settings.AnonymousPermitLimit;
            return RejectAsync(context, "global", partition.KindName, limit, ct);
        };
        options.AddPolicy(UiPolicyName, new UiPolicy(settings.AnonymousPermitLimit));
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
