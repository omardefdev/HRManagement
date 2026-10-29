using System.Security.Claims;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace HRManagement.UnitTests;

public class GatewayUpstreamCookieForwardingTests
{
    private const string BrowserCookies =
        ".AspNetCore.Cookies=abc; .AspNetCore.Antiforgery.xyz=token1; careers_session=CfDJ8-session; KEYCLOAK_SESSION=xyz";

    [Fact]
    public void BuildAntiforgeryCookieHeader_returns_null_for_empty_input()
    {
        Assert.Null(GatewayUpstreamCookieForwarding.BuildAntiforgeryCookieHeader(null));
        Assert.Null(GatewayUpstreamCookieForwarding.BuildAntiforgeryCookieHeader(""));
        Assert.Null(GatewayUpstreamCookieForwarding.BuildAntiforgeryCookieHeader("   "));
    }

    [Fact]
    public void BuildAntiforgeryCookieHeader_keeps_only_antiforgery_cookies()
    {
        var input =
            ".AspNetCore.Cookies=abc; .AspNetCore.Antiforgery.xyz=token1; KEYCLOAK_SESSION=xyz; .AspNetCore.Antiforgery.abc=token2";

        var result = GatewayUpstreamCookieForwarding.BuildAntiforgeryCookieHeader(input);

        Assert.Equal(".AspNetCore.Antiforgery.xyz=token1; .AspNetCore.Antiforgery.abc=token2", result);
    }

    [Fact]
    public void BuildAntiforgeryCookieHeader_returns_null_when_no_antiforgery_cookies()
    {
        var input = ".AspNetCore.Cookies=abc; KEYCLOAK_SESSION=xyz";

        Assert.Null(GatewayUpstreamCookieForwarding.BuildAntiforgeryCookieHeader(input));
    }

    // HCM-030 (PLAN-30 H5): careers_session is forwarded on the careers route only.

    [Fact]
    public void BuildAntiforgeryCookieHeader_never_forwards_the_careers_session_cookie()
    {
        Assert.Equal(
            ".AspNetCore.Antiforgery.xyz=token1",
            GatewayUpstreamCookieForwarding.BuildAntiforgeryCookieHeader(BrowserCookies));
        Assert.Null(GatewayUpstreamCookieForwarding.BuildAntiforgeryCookieHeader("careers_session=abc"));
    }

    [Fact]
    public void BuildUpstreamCookieHeader_on_the_careers_route_keeps_antiforgery_and_the_careers_session()
    {
        var result = GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader(BrowserCookies, forwardCareersSession: true);

        Assert.Equal(".AspNetCore.Antiforgery.xyz=token1; careers_session=CfDJ8-session", result);
        Assert.Equal(
            "careers_session=abc",
            GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader("careers_session=abc", forwardCareersSession: true));
    }

    [Fact]
    public void BuildUpstreamCookieHeader_off_the_careers_route_is_the_antiforgery_filter()
    {
        Assert.Equal(
            GatewayUpstreamCookieForwarding.BuildAntiforgeryCookieHeader(BrowserCookies),
            GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader(BrowserCookies, forwardCareersSession: false));
        Assert.Null(GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader("careers_session=abc", forwardCareersSession: false));
        Assert.Null(GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader(null, forwardCareersSession: true));
        Assert.Null(GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader("  ", forwardCareersSession: true));
    }

    [Fact]
    public void BuildUpstreamCookieHeader_on_the_careers_route_still_drops_every_other_cookie_name()
    {
        var input =
            "careers_session2=a; xcareers_session=b; Careers_Session=c; careers_session_old=d; .AspNetCore.Mvc.CookieTempDataProvider=e; "
            + ".AspNetCore.Cookies=f; .AspNetCore.Session=g; KEYCLOAK_SESSION=h; careers=i";

        Assert.Null(GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader(input, forwardCareersSession: true));
    }

    [Theory]
    [InlineData("careers-public", "/careers/{**catch-all}", true)]
    [InlineData("careers-public", null, true)]
    [InlineData("other-id", "/careers/{**catch-all}", true)]
    [InlineData(null, "/careers", true)]
    [InlineData(null, "/Careers/my/{**catch-all}", true)]
    [InlineData("hr-core-ui", "/ui/hr-core/{**catch-all}", false)]
    [InlineData("hr-core-api", "/api/hr-core/{**catch-all}", false)]
    [InlineData("careers-public-x", "/careersx/{**catch-all}", false)]
    [InlineData("Careers-Public", "/ui/careers/{**catch-all}", false)]
    [InlineData(null, null, false)]
    public void IsCareersRoute_matches_the_route_id_or_the_careers_path_segment(string? routeId, string? pathPattern, bool expected)
    {
        Assert.Equal(expected, GatewayUpstreamCookieForwarding.IsCareersRoute(routeId, pathPattern));
    }

    [Fact]
    public async Task Transform_forwards_careers_session_upstream_on_the_careers_route()
    {
        var upstreamCookie = await RunRequestTransformAsync("careers-public", "/careers/{**catch-all}", "/careers/my", BrowserCookies);

        Assert.Equal(".AspNetCore.Antiforgery.xyz=token1; careers_session=CfDJ8-session", upstreamCookie);
    }

    [Theory]
    [InlineData("hr-core-ui", "/ui/hr-core/{**catch-all}", "/ui/hr-core/Employees")]
    [InlineData("hr-core-api", "/api/hr-core/{**catch-all}", "/api/hr-core/employees")]
    [InlineData("ess-ui", "/ui/ess/{**catch-all}", "/ui/ess/")]
    public async Task Transform_drops_careers_session_on_every_other_route_and_keeps_antiforgery(
        string routeId,
        string pathPattern,
        string requestPath)
    {
        var upstreamCookie = await RunRequestTransformAsync(routeId, pathPattern, requestPath, BrowserCookies);

        Assert.Equal(".AspNetCore.Antiforgery.xyz=token1", upstreamCookie);
    }

    [Fact]
    public async Task Transform_sends_no_cookie_header_when_nothing_is_forwardable()
    {
        Assert.Null(await RunRequestTransformAsync(
            "careers-public", "/careers/{**catch-all}", "/careers/", ".AspNetCore.Cookies=abc; careers_session2=x"));
        Assert.Null(await RunRequestTransformAsync(
            "hr-core-ui", "/ui/hr-core/{**catch-all}", "/ui/hr-core/", "careers_session=abc"));
    }

    [Fact]
    public async Task Transform_never_forwards_staff_credentials_on_the_careers_route()
    {
        // HCM-030 (H5): a signed-in staff browser (gateway token) or a client Bearer header must not authenticate the
        // anonymous portal upstream, or HR.Core's tenant_id claim would win over the portal session tenant.
        var signedIn = await RunTransformAsync(
            "careers-public", "/careers/{**catch-all}", "/careers/my/login", BrowserCookies, accessToken: "staff-token");
        var clientBearer = await RunTransformAsync(
            "careers-public", "/careers/{**catch-all}", "/careers/my", BrowserCookies, incomingAuthorization: "Bearer client-token");

        Assert.Null(signedIn.Authorization);
        Assert.Null(clientBearer.Authorization);
        Assert.Equal(".AspNetCore.Antiforgery.xyz=token1; careers_session=CfDJ8-session", signedIn.Cookie);
    }

    [Theory]
    [InlineData("hr-core-ui", "/ui/hr-core/{**catch-all}", "/ui/hr-core/Employees")]
    [InlineData("hr-core-api", "/api/hr-core/{**catch-all}", "/api/hr-core/employees")]
    public async Task Transform_still_forwards_the_access_token_on_every_other_route(
        string routeId,
        string pathPattern,
        string requestPath)
    {
        var signedIn = await RunTransformAsync(routeId, pathPattern, requestPath, BrowserCookies, accessToken: "staff-token");
        var clientBearer = await RunTransformAsync(
            routeId, pathPattern, requestPath, BrowserCookies, incomingAuthorization: "Bearer client-token");

        Assert.Equal("Bearer staff-token", signedIn.Authorization);
        Assert.Equal("Bearer client-token", clientBearer.Authorization);
    }

    /// <summary>Builds the provider's transforms for a route and runs them on one request; returns the upstream Cookie header.</summary>
    private static async Task<string?> RunRequestTransformAsync(
        string routeId,
        string pathPattern,
        string requestPath,
        string cookieHeader) =>
        (await RunTransformAsync(routeId, pathPattern, requestPath, cookieHeader)).Cookie;

    /// <summary>
    /// Builds the provider's transforms for a route and runs them on one request; returns the upstream Cookie and
    /// Authorization headers. <paramref name="accessToken"/> simulates a signed-in gateway user (saved OIDC token).
    /// </summary>
    private static async Task<(string? Cookie, string? Authorization)> RunTransformAsync(
        string routeId,
        string pathPattern,
        string requestPath,
        string cookieHeader,
        string? accessToken = null,
        string? incomingAuthorization = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(new TokenAuthenticationService(accessToken))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var builderContext = new TransformBuilderContext
        {
            Services = services,
            Route = new RouteConfig { RouteId = routeId, ClusterId = "hr-core-cluster", Match = new RouteMatch { Path = pathPattern } },
        };
        new GatewayAccessTokenTransformProvider().Apply(builderContext);

        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Path = requestPath;
        httpContext.Request.Headers.Cookie = cookieHeader;
        if (incomingAuthorization is not null)
            httpContext.Request.Headers.Authorization = incomingAuthorization;

        using var proxyRequest = new HttpRequestMessage(HttpMethod.Get, "http://upstream" + requestPath);
        // As YARP copies request headers before transforms run, the browser Cookie (and Authorization) headers start on
        // the proxy request.
        proxyRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        if (incomingAuthorization is not null)
            proxyRequest.Headers.TryAddWithoutValidation("Authorization", incomingAuthorization);
        var transformContext = new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = proxyRequest,
            Path = httpContext.Request.Path,
            DestinationPrefix = "http://upstream",
        };

        foreach (var transform in builderContext.RequestTransforms)
            await transform.ApplyAsync(transformContext);

        return (
            proxyRequest.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : null,
            proxyRequest.Headers.Authorization?.ToString());
    }

    /// <summary>No gateway session, or (with a token) a signed-in gateway user whose OIDC tokens were saved.</summary>
    private sealed class TokenAuthenticationService(string? accessToken) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            if (accessToken is null)
                return Task.FromResult(AuthenticateResult.NoResult());
            var properties = new AuthenticationProperties();
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = accessToken }]);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", Guid.NewGuid().ToString("D"))], "Cookies"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, "Cookies")));
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
