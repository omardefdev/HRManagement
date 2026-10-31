using System.Diagnostics;
using System.Net;
using HRManagement.Gateway.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-35 final report defect 15, through the gateway's own pipeline: the first request over the per-client limit
/// (code default 120 per minute) is answered 429 at once with <c>Retry-After</c>, not held until the next window.
/// </summary>
public class GatewayRateLimitingIntegrationTests : IClassFixture<GatewayTestFactory>
{
    private readonly GatewayTestFactory _factory;

    public GatewayRateLimitingIntegrationTests(GatewayTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Request_over_the_limit_gets_429_at_once_with_retry_after()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        // The old queue held the request until the next one-minute window.
        client.Timeout = TimeSpan.FromSeconds(10);
        var limit = GatewayRateLimiting.PermitLimit(_factory.Services.GetRequiredService<IConfiguration>());

        for (var i = 0; i < limit; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);

        var watch = Stopwatch.StartNew();
        var rejected = await client.GetAsync("/health/live");
        watch.Stop();

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"The 429 took {watch.Elapsed}.");
        Assert.Equal(TimeSpan.FromSeconds(60), rejected.Headers.RetryAfter?.Delta);
        Assert.Equal(GatewayRateLimiting.RejectionText, await rejected.Content.ReadAsStringAsync());
    }
}
