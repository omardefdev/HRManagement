using HRManagement.Gateway.Middleware;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

public class GatewayAuditMiddlewareTests
{
    [Fact]
    public async Task Redacts_sensitive_query_and_marks_forbidden_decision()
    {
        var queue = new CapturingAuditQueue();
        var middleware = new GatewayAuditMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await Task.CompletedTask;
        });

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/hr-core/employees";
        ctx.Request.QueryString = new QueryString("?access_token=abc&q=test");
        ctx.Items[CorrelationIdMiddleware.ItemKey] = "corr-1";

        await middleware.InvokeAsync(ctx, queue, NoopSecurity.Instance, NullLogger<GatewayAuditMiddleware>.Instance);

        Assert.NotNull(queue.Last);
        Assert.Equal("forbidden", queue.Last!.Decision);
        Assert.Contains("access_token=REDACTED", queue.Last.QueryString);
        Assert.DoesNotContain("abc", queue.Last.QueryString);
    }

    private sealed class CapturingAuditQueue : IGatewayAuditQueue
    {
        public GatewayAuditEntry? Last { get; private set; }
        public int OverflowFileCount => 0;
        public int DlqFileCount => 0;
        public long PeekNextSequenceNumber() => 1;

        public bool TryEnqueue(GatewayAuditEntry entry)
        {
            Last = entry;
            return true;
        }
    }

    private sealed class NoopSecurity : ISecurityAuditPublisher
    {
        public static readonly NoopSecurity Instance = new();
        public Task PublishAsync(SecurityAuditEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public void RecordLookupHit(string? actorSub, string correlationId) { }
    }
}
