using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;

namespace HRManagement.UnitTests;

public class CorrelationIdMiddlewareExtendedTests
{
    [Fact]
    public async Task Generates_new_id_when_header_absent()
    {
        string? capturedId = null;
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            capturedId = ctx.Items[CorrelationIdMiddleware.ItemKey] as string;
            return Task.CompletedTask;
        });

        var ctx = new DefaultHttpContext();
        await middleware.Invoke(ctx);

        Assert.NotNull(capturedId);
        Assert.NotEmpty(capturedId);
        Assert.Equal(capturedId, ctx.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task Sets_CorrelationIdFlow_value()
    {
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            Assert.Equal("flow-test", CorrelationIdFlow.Get());
            return Task.CompletedTask;
        });

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[CorrelationIdMiddleware.HeaderName] = "flow-test";

        await middleware.Invoke(ctx);
    }

    [Fact]
    public async Task Ignores_whitespace_only_header()
    {
        string? capturedId = null;
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            capturedId = ctx.Items[CorrelationIdMiddleware.ItemKey] as string;
            return Task.CompletedTask;
        });

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[CorrelationIdMiddleware.HeaderName] = "   ";

        await middleware.Invoke(ctx);

        Assert.NotNull(capturedId);
        Assert.NotEqual("   ", capturedId);
    }
}
