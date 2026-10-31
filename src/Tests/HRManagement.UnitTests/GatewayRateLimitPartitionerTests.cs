using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using HRManagement.Gateway.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-36 §4 (G-OPS-10): a demo room behind one NAT. Signed-in users get one window each, anonymous callers one per
/// IP, and static assets are not limited. Runs the real rate limiting middleware with the gateway's options.
/// </summary>
public sealed class GatewayRateLimitPartitionerTests
{
    private const string RoomNat = "198.51.100.20";
    private const string PagePath = "/ui/hr-core/Employees";

    [Fact]
    public async Task Two_users_behind_one_ip_have_separate_quotas()
    {
        using var gateway = new LimitedGateway(new GatewayRateLimitSettings(30, 40, ExemptStaticAssets: true));

        for (var i = 0; i < 40; i++)
            Assert.Equal(StatusCodes.Status200OK, await gateway.SendAsync(RoomNat, PagePath, subject: "user-a"));

        // user-a has used the whole authenticated window; user-b, from the same address, has not.
        Assert.Equal(StatusCodes.Status429TooManyRequests, await gateway.SendAsync(RoomNat, PagePath, subject: "user-a"));
        for (var i = 0; i < 40; i++)
            Assert.Equal(StatusCodes.Status200OK, await gateway.SendAsync(RoomNat, PagePath, subject: "user-b"));

        // And the anonymous window of that address is untouched by both.
        Assert.Equal(StatusCodes.Status200OK, await gateway.SendAsync(RoomNat, PagePath));

        var warning = Assert.Single(gateway.Warnings);
        Assert.Contains("(user partition, 40 per minute)", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("user-a", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(RoomNat, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_callers_get_429_with_retry_after_at_the_limit()
    {
        using var gateway = new LimitedGateway(new GatewayRateLimitSettings(30, 600, ExemptStaticAssets: true));

        for (var i = 0; i < 30; i++)
            Assert.Equal(StatusCodes.Status200OK, await gateway.SendAsync(RoomNat, PagePath));

        Assert.Equal(StatusCodes.Status429TooManyRequests, await gateway.SendAsync(RoomNat, PagePath));
        Assert.Equal("60", gateway.LastRetryAfter);
        Assert.Contains("(ip partition, 30 per minute)", Assert.Single(gateway.Warnings), StringComparison.Ordinal);

        // A signed-in user behind the same address is not affected by the anonymous flood.
        Assert.Equal(StatusCodes.Status200OK, await gateway.SendAsync(RoomNat, PagePath, subject: "user-a"));
    }

    [Theory]
    [InlineData("/ui/hr-core/css/site.css")]
    [InlineData("/ui/payroll/lib/bootstrap/dist/js/bootstrap.bundle.min.js")]
    [InlineData("/ui/hr-core/fonts/Cairo.woff2")]
    [InlineData("/ui/idcard/images/logo.PNG")]
    [InlineData("/css/site.css")]
    [InlineData("/favicon.ico")]
    [InlineData("/js/site.js.map")]
    public async Task Static_assets_are_exempt(string path)
    {
        using var gateway = new LimitedGateway(new GatewayRateLimitSettings(30, 30, ExemptStaticAssets: true));

        for (var i = 0; i < 100; i++)
            Assert.Equal(StatusCodes.Status200OK, await gateway.SendAsync(RoomNat, path));

        Assert.Empty(gateway.Warnings);
    }

    [Theory]
    [InlineData("/api/x.css")]
    [InlineData("/api/files/report.js")]
    [InlineData("/careers/css/site.css")]
    [InlineData("/ui/hr-core.css")]
    [InlineData("/ui/x.js")]
    [InlineData("/ui/hr-core/Employees")]
    [InlineData("/ui/hr-core/")]
    [InlineData("/health/ready.js")]
    public void Lookalike_paths_are_not_assets(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        Assert.False(GatewayRateLimitPartitioner.IsStaticAsset(context.Request));
    }

    [Fact]
    public async Task Lookalike_api_path_counts_against_the_window()
    {
        using var gateway = new LimitedGateway(new GatewayRateLimitSettings(30, 30, ExemptStaticAssets: true));
        for (var i = 0; i < 30; i++)
            await gateway.SendAsync(RoomNat, "/api/x.css");

        Assert.Equal(StatusCodes.Status429TooManyRequests, await gateway.SendAsync(RoomNat, "/api/x.css"));
    }

    [Fact]
    public void Asset_exemption_is_get_and_head_only_and_can_be_switched_off()
    {
        var post = new DefaultHttpContext();
        post.Request.Method = HttpMethods.Post;
        post.Request.Path = "/ui/hr-core/css/site.css";
        Assert.False(GatewayRateLimitPartitioner.IsStaticAsset(post.Request));

        var get = new DefaultHttpContext();
        get.Request.Method = HttpMethods.Head;
        get.Request.Path = "/ui/hr-core/css/site.css";
        get.Connection.RemoteIpAddress = IPAddress.Parse(RoomNat);
        Assert.Equal(GatewayRateLimitPartitionKind.Exempt, GatewayRateLimitPartitioner.Resolve(get, exemptStaticAssets: true).Kind);
        Assert.Equal(GatewayRateLimitPartitionKind.Ip, GatewayRateLimitPartitioner.Resolve(get, exemptStaticAssets: false).Kind);
    }

    [Fact]
    public void Partition_key_comes_from_the_authenticated_subject_not_from_request_headers()
    {
        var anonymous = new DefaultHttpContext();
        anonymous.Request.Method = HttpMethods.Get;
        anonymous.Request.Path = PagePath;
        anonymous.Connection.RemoteIpAddress = IPAddress.Parse(RoomNat);
        anonymous.Request.Headers["X-User"] = "someone";
        anonymous.Request.Headers["sub"] = "someone";
        // An unauthenticated identity carrying a sub claim is still anonymous.
        anonymous.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "forged")]));

        var partition = GatewayRateLimitPartitioner.Resolve(anonymous, exemptStaticAssets: true);
        Assert.Equal(GatewayRateLimitPartitionKind.Ip, partition.Kind);
        Assert.Equal("ip:" + RoomNat, partition.Key);

        var signedIn = new DefaultHttpContext();
        signedIn.Request.Method = HttpMethods.Get;
        signedIn.Request.Path = PagePath;
        signedIn.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "nameid-1")], "Cookies"));
        Assert.Equal("user:nameid-1", GatewayRateLimitPartitioner.Resolve(signedIn, exemptStaticAssets: true).Key);
    }

    [Theory]
    [InlineData(null, null, 120, 600)]
    [InlineData("300", "600", 300, 600)]
    [InlineData("0", "0", 30, 30)]
    [InlineData("10", "5000", 30, 2000)]
    [InlineData("99999", "-1", 2000, 30)]
    public void Both_limits_are_clamped_to_30_to_2000(string? anonymous, string? authenticated, int expectedAnonymous, int expectedAuthenticated)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GatewayRateLimiting.PermitLimitKey] = anonymous,
                [GatewayRateLimiting.AuthenticatedPermitLimitKey] = authenticated
            })
            .Build();

        var settings = GatewayRateLimiting.Settings(configuration);
        Assert.Equal(expectedAnonymous, settings.AnonymousPermitLimit);
        Assert.Equal(expectedAuthenticated, settings.AuthenticatedPermitLimit);
        Assert.True(settings.ExemptStaticAssets);
    }

    [Fact]
    public void Limiter_runs_after_authentication_in_the_gateway_pipeline()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !System.IO.File.Exists(Path.Combine(root.FullName, "HRManagement.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var program = System.IO.File.ReadAllText(Path.Combine(root!.FullName, "src", "Gateway", "HRManagement.Gateway", "Program.cs"));

        var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var limiter = program.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);
        Assert.True(authentication > 0, "UseAuthentication not found");
        Assert.True(limiter > authentication, "UseRateLimiter must run after UseAuthentication");
        Assert.Equal(limiter, program.LastIndexOf("app.UseRateLimiter();", StringComparison.Ordinal));
        Assert.True(limiter < program.IndexOf("app.MapReverseProxy();", StringComparison.Ordinal));
    }

    /// <summary>Status code pages, authentication stand-in, the limiter, and a terminal that answers 200.</summary>
    private sealed class LimitedGateway : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly RequestDelegate _pipeline;
        private readonly ConcurrentQueue<(string Category, LogLevel Level, string Message)> _logs = new();

        public LimitedGateway(GatewayRateLimitSettings settings)
        {
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddProvider(new Collector(_logs)));
            services.AddRateLimiter(options => GatewayRateLimiting.Configure(options, settings));
            _services = services.BuildServiceProvider();

            var app = new ApplicationBuilder(_services);
            app.UseStatusCodePagesWithReExecute("/Home/ErrorStatus/{0}");
            app.UseRateLimiter();
            app.Run(context => context.Response.WriteAsync("passed"));
            _pipeline = app.Build();
        }

        public string? LastRetryAfter { get; private set; }

        public IReadOnlyList<string> Warnings =>
            _logs.Where(e => e.Level == LogLevel.Warning && e.Category == typeof(GatewayRateLimiting).FullName)
                .Select(e => e.Message)
                .ToList();

        public async Task<int> SendAsync(string clientIp, string path, string? subject = null)
        {
            var http = new DefaultHttpContext { RequestServices = _services };
            http.Connection.RemoteIpAddress = IPAddress.Parse(clientIp);
            http.Request.Method = HttpMethods.Get;
            http.Request.Path = path;
            http.Response.Body = new MemoryStream();
            if (subject is not null)
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "Cookies"));

            await _pipeline(http);
            var retryAfter = http.Response.Headers.RetryAfter.ToString();
            LastRetryAfter = retryAfter.Length > 0 ? retryAfter : null;
            return http.Response.StatusCode;
        }

        public void Dispose()
        {
            _services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter?.Dispose();
            _services.Dispose();
        }
    }

    private sealed class Collector(ConcurrentQueue<(string Category, LogLevel Level, string Message)> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(entries, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<(string, LogLevel, string)> entries, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }
}
