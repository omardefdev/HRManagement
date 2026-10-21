using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults.Integration;
using Microsoft.Extensions.Configuration;

namespace HRManagement.UnitTests;

public class HrCoreLookupClientTests
{
    [Fact]
    public async Task GetEmployeesByIdsAsync_deserializes_camelCase_json()
    {
        var employeeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"items":[{"id":"{{employeeId}}","label":"أحمد محمد","sublabel":"IT"}],"hasMore":false}""",
                System.Text.Encoding.UTF8,
                "application/json")
        });

        var client = new HrCoreLookupClient(new FixedClientFactory(handler), new ConfigurationBuilder().Build());
        var items = await client.GetEmployeesByIdsAsync([employeeId]);

        var item = Assert.Single(items);
        Assert.Equal(employeeId, item.Id);
        Assert.Equal("أحمد محمد", item.Label);
        Assert.Equal("IT", item.Sublabel);
    }

    [Fact]
    public async Task GetEmployeesByIdsAsync_chunks_120_ids_into_3_requests()
    {
        var requestCount = 0;
        var handler = new CountingHandler(_ =>
        {
            requestCount++;
            var payload = JsonSerializer.Serialize(new
            {
                items = new[] { new { id = Guid.NewGuid(), label = "Test", sublabel = (string?)null } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var factory = new FixedClientFactory(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var client = new HrCoreLookupClient(factory, config);

        var ids = Enumerable.Range(0, 120).Select(_ => Guid.NewGuid()).ToList();
        var items = await client.GetEmployeesByIdsAsync(ids);

        Assert.Equal(3, requestCount);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task GetEmployeesByIdsAsync_returns_empty_for_no_ids()
    {
        var handler = new CountingHandler(_ => throw new InvalidOperationException("Should not be called"));
        var factory = new FixedClientFactory(handler);
        var config = new ConfigurationBuilder().Build();
        var client = new HrCoreLookupClient(factory, config);

        var items = await client.GetEmployeesByIdsAsync([]);

        Assert.Empty(items);
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("/api/lookup/employees/by-ids", request.RequestUri?.AbsolutePath);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class FixedClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://hr-core/") };
    }
}
