using HRManagement.ServiceDefaults;

namespace HRManagement.UnitTests;

public class GatewayUpstreamCookieForwardingTests
{
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
}
