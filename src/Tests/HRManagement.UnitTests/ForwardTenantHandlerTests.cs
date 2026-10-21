using System.Net;
using System.Net.Http.Headers;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;

namespace HRManagement.UnitTests;

public class ForwardTenantHandlerTests
{
    [Fact]
    public async Task SendAsync_forwards_tenant_header_without_replacing_service_bearer()
    {
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var context = new DefaultHttpContext();
        context.Items[TenantIdMiddleware.ItemKey] = tenantId;
        var handler = CreateHandler(context, out var inner);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://hr-core-web:8080/api/lookup/employees/by-ids");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "svc-payroll-token");
        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(inner.Requests);
        Assert.Equal(tenantId.ToString("D"), forwarded.Headers.GetValues(TenantIdMiddleware.HeaderName).Single());
        var auth = forwarded.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal("svc-payroll-token", auth.Parameter);
    }

    [Fact]
    public async Task SendAsync_does_not_overwrite_existing_tenant_header()
    {
        var tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var context = new DefaultHttpContext();
        context.Items[TenantIdMiddleware.ItemKey] = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = CreateHandler(context, out var inner);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://hr-core-web:8080/api/lookup/employees/by-ids");
        request.Headers.TryAddWithoutValidation(TenantIdMiddleware.HeaderName, tenantId.ToString("D"));
        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(inner.Requests);
        Assert.Equal(tenantId.ToString("D"), forwarded.Headers.GetValues(TenantIdMiddleware.HeaderName).Single());
    }

    private static ForwardTenantHandler CreateHandler(HttpContext context, out RecordingHandler inner)
    {
        inner = new RecordingHandler();
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new ForwardTenantHandler(accessor) { InnerHandler = inner };
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
