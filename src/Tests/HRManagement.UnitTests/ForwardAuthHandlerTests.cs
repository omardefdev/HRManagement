using System.Net;
using System.Net.Http.Headers;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;

namespace HRManagement.UnitTests;

public class ForwardAuthHandlerTests
{
    [Fact]
    public async Task SendAsync_forwards_incoming_bearer_token()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer incoming-token";
        var handler = CreateHandler(context, out var inner);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://hr-gateway:8080/api/files/x/references");
        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(inner.Requests).Headers.Authorization;
        Assert.NotNull(forwarded);
        Assert.Equal("Bearer", forwarded.Scheme);
        Assert.Equal("incoming-token", forwarded.Parameter);
    }

    [Fact]
    public async Task SendAsync_does_not_forward_cookie()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = ".AspNetCore.Cookies=fake-session";
        var handler = CreateHandler(context, out var inner);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://hr-gateway:8080/api/files/x/references");
        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(inner.Requests);
        Assert.False(forwarded.Headers.Contains("Cookie"));
        Assert.Null(forwarded.Headers.Authorization);
    }

    private static ForwardAuthHandler CreateHandler(HttpContext context, out RecordingHandler inner)
    {
        inner = new RecordingHandler();
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new ForwardAuthHandler(accessor) { InnerHandler = inner };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
