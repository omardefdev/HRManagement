using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
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
/// PLAN-35 final report defect 15: the gateway answers a request over its limit with 429 and <c>Retry-After</c> at
/// once, instead of queueing it until the next one-minute window. Runs the real rate limiting middleware with the
/// gateway's options, behind status code pages as in the gateway pipeline.
/// </summary>
public sealed class GatewayRateLimitingTests
{
    private const string Client = "203.0.113.7";
    private const string OtherClient = "203.0.113.8";
    private const string ModulePath = "/ui/hr-core/Employees";

    [Fact]
    public async Task Requests_within_the_limit_reach_the_gateway_unchanged()
    {
        using var gateway = new LimitedGateway(permitLimit: 3);

        for (var i = 0; i < 3; i++)
        {
            var sent = await gateway.SendAsync(Client);
            Assert.Equal(StatusCodes.Status200OK, sent.StatusCode);
            Assert.Null(sent.RetryAfter);
            Assert.Equal("passed", sent.Body);
        }

        Assert.Equal(3, gateway.Passed.Count);
        Assert.Empty(gateway.Warnings);
    }

    [Fact]
    public async Task First_request_over_the_limit_gets_429_at_once_with_retry_after()
    {
        using var gateway = new LimitedGateway(permitLimit: 3);
        for (var i = 0; i < 3; i++)
            await gateway.SendAsync(Client);

        var rejected = await gateway.SendAsync(Client);

        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Elapsed < TimeSpan.FromSeconds(2), $"The 429 took {rejected.Elapsed}.");
        Assert.Equal("60", rejected.RetryAfter);
        Assert.Equal("text/plain; charset=utf-8", rejected.ContentType);
        Assert.Equal(GatewayRateLimiting.RejectionText, rejected.Body);
        Assert.Equal(3, gateway.Passed.Count);

        var warning = Assert.Single(gateway.Warnings);
        Assert.Equal(
            $"Gateway rate limit: {ModulePath} answered 429 by the global limiter (ip partition, 3 per minute), retry after 60 s",
            warning);
        Assert.DoesNotContain(Client, warning, StringComparison.Ordinal);

        // The window is per client IP: another client is not affected.
        Assert.Equal(StatusCodes.Status200OK, (await gateway.SendAsync(OtherClient)).StatusCode);
    }

    [Fact]
    public async Task The_429_is_not_sent_through_the_limiter_again_as_the_status_page()
    {
        using var gateway = new LimitedGateway(permitLimit: 3);
        for (var i = 0; i < 3; i++)
            await gateway.SendAsync(Client);

        var rejected = await gateway.SendAsync(Client);

        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.StatusCode);
        Assert.DoesNotContain("/Home/ErrorStatus/429", gateway.SeenByLimiter);
        Assert.Single(gateway.Warnings);
    }

    [Fact]
    public async Task Gateway_ui_policy_answers_429_at_once_too()
    {
        using var gateway = new LimitedGateway(permitLimit: 3);
        var uiRoute = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(GatewayRateLimiting.UiPolicyName)),
            "gateway-ui route");

        // One window for all callers: three clients, each within its own per-IP window, use it up.
        foreach (var client in new[] { "203.0.113.1", "203.0.113.2", "203.0.113.3" })
            Assert.Equal(StatusCodes.Status200OK, (await gateway.SendAsync(client, uiRoute)).StatusCode);

        var rejected = await gateway.SendAsync("203.0.113.4", uiRoute);

        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Elapsed < TimeSpan.FromSeconds(2), $"The 429 took {rejected.Elapsed}.");
        Assert.Equal("60", rejected.RetryAfter);
        Assert.Equal(GatewayRateLimiting.RejectionText, rejected.Body);
        Assert.Contains("by the gateway-ui limiter (shared partition, 3 per minute)", Assert.Single(gateway.Warnings), StringComparison.Ordinal);

        // A route without the policy only counts against the per-IP window.
        Assert.Equal(StatusCodes.Status200OK, (await gateway.SendAsync("203.0.113.4")).StatusCode);
    }

    [Theory]
    [InlineData(null, 120)]
    [InlineData("10", 30)]
    [InlineData("500", 500)]
    [InlineData("2000", 2000)]
    [InlineData("5000", 2000)]
    public void Permit_limit_comes_from_configuration_within_30_to_2000(string? configured, int expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [GatewayRateLimiting.PermitLimitKey] = configured })
            .Build();

        Assert.Equal(expected, GatewayRateLimiting.PermitLimit(configuration));
    }

    [Fact]
    public void Gateway_program_uses_these_limits_and_the_status_page_shows_the_same_text()
    {
        var gateway = Path.Combine(ResolveRepoRoot(), "src", "Gateway", "HRManagement.Gateway");
        var program = System.IO.File.ReadAllText(Path.Combine(gateway, "Program.cs"));
        Assert.Contains("GatewayRateLimiting.PermitLimit(builder.Configuration)", program, StringComparison.Ordinal);
        Assert.Contains("GatewayRateLimiting.Configure(options, ratePermit)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("QueueLimit", program, StringComparison.Ordinal);

        var statusPage = System.IO.File.ReadAllText(Path.Combine(gateway, "Views", "Home", "Status.cshtml"));
        Assert.Contains($"429 => \"{GatewayRateLimiting.RejectionText}\"", statusPage, StringComparison.Ordinal);
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "src", "Gateway", "HRManagement.Gateway", "Program.cs")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private sealed record Sent(int StatusCode, string? RetryAfter, string? ContentType, string Body, TimeSpan Elapsed);

    /// <summary>Status code pages, the limiter, and a terminal that answers "passed" (the gateway's order).</summary>
    private sealed class LimitedGateway : IDisposable
    {
        private static readonly TimeSpan HeldTooLong = TimeSpan.FromSeconds(5);
        private readonly ServiceProvider _services;
        private readonly RequestDelegate _pipeline;
        private readonly LogCollector _logs = new();

        public LimitedGateway(int permitLimit)
        {
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddProvider(_logs));
            services.AddRateLimiter(options => GatewayRateLimiting.Configure(options, permitLimit));
            _services = services.BuildServiceProvider();

            var app = new ApplicationBuilder(_services);
            app.UseStatusCodePagesWithReExecute("/Home/ErrorStatus/{0}");
            app.Use(async (context, next) =>
            {
                SeenByLimiter.Enqueue(context.Request.Path.Value!);
                await next(context);
            });
            app.UseRateLimiter();
            app.Run(async context =>
            {
                Passed.Enqueue(context.Request.Path.Value!);
                await context.Response.WriteAsync("passed");
            });
            _pipeline = app.Build();
        }

        public ConcurrentQueue<string> SeenByLimiter { get; } = new();

        public ConcurrentQueue<string> Passed { get; } = new();

        public IReadOnlyList<string> Warnings =>
            _logs.Entries
                .Where(e => e.Level == LogLevel.Warning && e.Category == typeof(GatewayRateLimiting).FullName)
                .Select(e => e.Message)
                .ToList();

        public async Task<Sent> SendAsync(string clientIp, Endpoint? endpoint = null)
        {
            var http = new DefaultHttpContext { RequestServices = _services };
            http.Connection.RemoteIpAddress = IPAddress.Parse(clientIp);
            http.Request.Method = HttpMethods.Get;
            http.Request.Path = ModulePath;
            http.Response.Body = new MemoryStream();
            if (endpoint is not null)
                http.SetEndpoint(endpoint);
            using var abort = new CancellationTokenSource();
            http.RequestAborted = abort.Token;

            var watch = Stopwatch.StartNew();
            var pending = _pipeline(http);
            // The old queue held the request until the next one-minute window; stop waiting long before that.
            if (await Task.WhenAny(pending, Task.Delay(HeldTooLong)) != pending)
            {
                await abort.CancelAsync();
                Assert.Fail($"The limiter held a request from {clientIp} for {HeldTooLong} instead of answering it.");
            }
            await pending;
            watch.Stop();

            http.Response.Body.Position = 0;
            var body = await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync();
            var retryAfter = http.Response.Headers.RetryAfter.ToString();
            return new Sent(
                http.Response.StatusCode,
                retryAfter.Length > 0 ? retryAfter : null,
                http.Response.ContentType,
                body,
                watch.Elapsed);
        }

        public void Dispose()
        {
            _services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter?.Dispose();
            _services.Dispose();
        }
    }

    private sealed class LogCollector : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Collector(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Collector(LogCollector owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }
}
