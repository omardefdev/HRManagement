using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;

namespace HRManagement.UnitTests;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Uses_incoming_header_when_present()
    {
        var invoked = false;
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            invoked = true;
            Assert.Equal("abc-123", ctx.Items[CorrelationIdMiddleware.ItemKey]);
            return Task.CompletedTask;
        });
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc-123";

        await middleware.Invoke(ctx);

        Assert.True(invoked);
        Assert.Equal("abc-123", ctx.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }
}
