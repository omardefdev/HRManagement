using System.Net;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace HRManagement.UnitTests;

public class GatewayHealthProbeServiceTests
{
    [Fact]
    public async Task Marks_module_healthy_when_upstream_health_is_healthy()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Healthy") }));
        var factory = new FakeHttpClientFactory(httpClient);
        var memory = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gateway:HealthProbe:TimeoutSeconds"] = "2",
            ["Gateway:HealthProbe:CacheSeconds"] = "1"
        }).Build();
        var service = new GatewayHealthProbeService(factory, memory, config);

        var module = new GatewayModule("hr-core", "HR.Core", "نواة الموارد البشرية", "/ui/hr-core", 5010, "/health/upstreams/hr-core", "x", "hr-core");
        var result = await service.ProbeAsync([module], "http://localhost:5000", _ => true, CancellationToken.None);

        Assert.True(result["hr-core"].IsHealthy);
        Assert.Equal(200, result["hr-core"].StatusCode);
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
