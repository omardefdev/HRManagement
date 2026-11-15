using System.Net;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-67 (D-67-1, G-UX-01, P-4): one Content-Security-Policy per response. A page the gateway forwards keeps the policy and the
/// nonce of the service that rendered it; a page the gateway renders, or a response that arrives without the headers, gets the
/// gateway's. Before PLAN-67 the gateway wrote its policy before forwarding and YARP added the service's next to it: two policies,
/// and every inline block of a service page had to satisfy both (the demo's unstyled portal).
/// </summary>
public sealed class Ux067GatewaySecurityHeadersTests : IAsyncLifetime
{
    private const string DestinationPolicy = "default-src 'self'; script-src 'self' 'nonce-DESTINATION'; style-src 'self' 'nonce-DESTINATION'";
    private WebApplication? _destination;
    private string _destinationUrl = "";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _destination = builder.Build();
        _destination.MapGet("/{**rest}", (HttpContext context) =>
        {
            context.Response.Headers[HrSecurityHeadersExtensions.CspHeader] = DestinationPolicy;
            context.Response.Headers["X-Frame-Options"] = "DENY";
            return Results.Content("<html><head><style nonce=\"DESTINATION\">.careers-card{}</style></head><body>service page</body></html>", "text/html");
        });
        _destination.MapGet("/bare/{**rest}", () => Results.Content("<html><body>no headers</body></html>", "text/html"));
        await _destination.StartAsync();
        _destinationUrl = _destination.Urls.First();
    }

    public async Task DisposeAsync()
    {
        if (_destination is not null)
            await _destination.DisposeAsync();
    }

    /// <summary>A gateway built with the shared middleware and YARP's forwarder: the mechanism alone.</summary>
    private async Task<WebApplication> GatewayAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:CspMode"] = "Enforce" });
        builder.Services.AddHrSecurityHeaders(builder.Configuration);
        builder.Services.AddHttpForwarder();
        var app = builder.Build();
        app.UseHrSecurityHeaders();
        var invoker = new HttpMessageInvoker(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false });
        app.Map("/proxied/{**rest}", async (HttpContext context, IHttpForwarder forwarder) =>
        {
            context.Request.Path = "/" + (context.Request.RouteValues["rest"] as string ?? "");
            await forwarder.SendAsync(context, _destinationUrl, invoker, ForwarderRequestConfig.Empty, HttpTransformer.Default);
        });
        app.Map("/proxied-bare/{**rest}", async (HttpContext context, IHttpForwarder forwarder) =>
        {
            context.Request.Path = "/bare/" + (context.Request.RouteValues["rest"] as string ?? "");
            await forwarder.SendAsync(context, _destinationUrl, invoker, ForwarderRequestConfig.Empty, HttpTransformer.Default);
        });
        app.MapGet("/own", (HttpContext context) =>
            Results.Content($"<html><head><script nonce=\"{context.GetCspNonce()}\">var x = 1;</script></head><body>gateway page</body></html>", "text/html"));
        await app.StartAsync();
        return app;
    }

    private static IReadOnlyList<string> Values(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.ToList()
        : response.Content.Headers.TryGetValues(name, out var content) ? content.ToList()
        : [];

    [Fact]
    public async Task A_forwarded_page_keeps_the_service_policy_alone()
    {
        await using var gateway = await GatewayAsync();
        using var client = gateway.GetTestClient();

        using var response = await client.GetAsync("/proxied/careers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var policies = Values(response, HrSecurityHeadersExtensions.CspHeader);
        Assert.Equal([DestinationPolicy], policies);
        Assert.Empty(Values(response, HrSecurityHeadersExtensions.CspReportOnlyHeader));
        Assert.Equal(["DENY"], Values(response, "X-Frame-Options"));
        Assert.Equal(["nosniff"], Values(response, "X-Content-Type-Options"));
        Assert.Contains("nonce=\"DESTINATION\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_only_policy_behind_an_enforcing_host_is_enforced_as_it_is_and_otherwise_left_alone()
    {
        // Review finding 3: a service left in ReportOnly behind an enforcing gateway would serve no enforced policy.
        var forwarded = new HeaderDictionary { [HrSecurityHeadersExtensions.CspReportOnlyHeader] = DestinationPolicy };
        HrSecurityHeadersExtensions.ApplySecurityHeaders(forwarded, HrSecurityHeadersExtensions.CspHeader, "default-src 'self'; script-src 'self' 'nonce-GATEWAY'");
        Assert.Equal(DestinationPolicy, forwarded[HrSecurityHeadersExtensions.CspHeader].ToString());
        Assert.False(forwarded.ContainsKey(HrSecurityHeadersExtensions.CspReportOnlyHeader));

        // A host that itself only reports leaves a report-only answer as it is.
        var reporting = new HeaderDictionary { [HrSecurityHeadersExtensions.CspReportOnlyHeader] = DestinationPolicy };
        HrSecurityHeadersExtensions.ApplySecurityHeaders(reporting, HrSecurityHeadersExtensions.CspReportOnlyHeader, "default-src 'self'");
        Assert.Equal(DestinationPolicy, reporting[HrSecurityHeadersExtensions.CspReportOnlyHeader].ToString());
        Assert.False(reporting.ContainsKey(HrSecurityHeadersExtensions.CspHeader));

        // An enforced policy is never touched.
        var enforced = new HeaderDictionary { [HrSecurityHeadersExtensions.CspHeader] = DestinationPolicy };
        HrSecurityHeadersExtensions.ApplySecurityHeaders(enforced, HrSecurityHeadersExtensions.CspHeader, "default-src 'self'");
        Assert.Equal(DestinationPolicy, enforced[HrSecurityHeadersExtensions.CspHeader].ToString());
    }

    [Fact]
    public async Task A_forwarded_response_without_headers_gets_the_gateway_headers_once()
    {
        await using var gateway = await GatewayAsync();
        using var client = gateway.GetTestClient();

        using var response = await client.GetAsync("/proxied-bare/x");

        var policies = Values(response, HrSecurityHeadersExtensions.CspHeader);
        Assert.Single(policies);
        Assert.Contains("script-src 'self' 'nonce-", policies[0], StringComparison.Ordinal);
        Assert.Equal(["SAMEORIGIN"], Values(response, "X-Frame-Options"));
        Assert.Equal(["strict-origin-when-cross-origin"], Values(response, "Referrer-Policy"));
    }

    [Fact]
    public async Task A_gateway_page_carries_the_gateway_policy_whose_nonce_the_page_uses()
    {
        await using var gateway = await GatewayAsync();
        using var client = gateway.GetTestClient();

        using var response = await client.GetAsync("/own");

        var policies = Values(response, HrSecurityHeadersExtensions.CspHeader);
        Assert.Single(policies);
        var headerNonce = Regex.Match(policies[0], "script-src 'self' 'nonce-(?<n>[^']+)'").Groups["n"].Value;
        var pageNonce = Regex.Match(await response.Content.ReadAsStringAsync(), "nonce=\"(?<n>[^\"]+)\"").Groups["n"].Value;
        Assert.False(string.IsNullOrEmpty(headerNonce));
        Assert.Equal(headerNonce, pageNonce);
        Assert.DoesNotContain("unsafe-inline", policies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_real_gateway_forwards_the_public_portal_with_one_policy()
    {
        await using var baseFactory = new GatewayTestFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:CspMode"] = "Enforce",
                ["ReverseProxy:Clusters:hr-core-cluster:Destinations:destination1:Address"] = _destinationUrl,
            })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/careers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([DestinationPolicy], Values(response, HrSecurityHeadersExtensions.CspHeader));
        Assert.Equal(["DENY"], Values(response, "X-Frame-Options"));
    }
}
