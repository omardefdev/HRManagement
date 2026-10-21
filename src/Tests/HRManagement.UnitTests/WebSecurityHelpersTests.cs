using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace HRManagement.UnitTests;

public class WebSecurityHelpersTests
{
    private static IUrlHelper CreateUrlHelper()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new UrlHelper(actionContext);
    }

    [Theory]
    [InlineData("/ui/hr-core/reports", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("//evil.example", false)]
    [InlineData("/ui/other-module", false)]
    public void SafeRedirect_accepts_only_module_relative_paths(string candidate, bool expectedOk)
    {
        var url = CreateUrlHelper();
        var ok = SafeRedirect.TryResolveModuleUiPath(url, "/ui/hr-core/", candidate, out var target);
        Assert.Equal(expectedOk, ok);
        if (expectedOk)
            Assert.StartsWith("/ui/hr-core", target, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrganizationBrandingSanitizer_rejects_injection()
    {
        Assert.Equal("#6b1f2a", OrganizationBrandingSanitizer.SanitizePrimaryColor("red; url(javascript:alert(1))"));
        Assert.Equal("#aabbcc", OrganizationBrandingSanitizer.SanitizePrimaryColor("#aabbcc"));
    }

    [Fact]
    public void GatewayCallerProof_roundtrip()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GatewayCallerProof:Secret"] = "test-secret-key" })
            .Build();

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gateway.local/ui/hr-core/Reports/ExportEmployeesCsv");
        GatewayCallerProof.Sign(request, config);

        var proof = request.Headers.GetValues(GatewayCallerProof.ProofHeader).First();
        var ts = request.Headers.GetValues(GatewayCallerProof.TimestampHeader).First();

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/ui/hr-core/Reports/ExportEmployeesCsv";
        context.Request.Headers[GatewayCallerProof.ProofHeader] = proof;
        context.Request.Headers[GatewayCallerProof.TimestampHeader] = ts;
        context.Request.Headers[GatewayCallerProof.PathHeader] =
            request.Headers.GetValues(GatewayCallerProof.PathHeader).Single();

        Assert.True(GatewayCallerProof.Validate(context, config, enforce: true));
    }

    [Fact]
    public void GatewayCallerProof_roundtrip_with_explicit_transformed_path()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GatewayCallerProof:Secret"] = "test-secret-key" })
            .Build();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://hr-core-web:8080/");
        const string transformedPath = "/api/lookup/employees?q=%D9%81%D8%B1%D8%B9&page=1";

        GatewayCallerProof.Sign(request, config, transformedPath);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/lookup/employees";
        context.Request.QueryString = new QueryString("?q=%D9%81%D8%B1%D8%B9&page=1");
        context.Request.Headers[GatewayCallerProof.ProofHeader] =
            request.Headers.GetValues(GatewayCallerProof.ProofHeader).Single();
        context.Request.Headers[GatewayCallerProof.TimestampHeader] =
            request.Headers.GetValues(GatewayCallerProof.TimestampHeader).Single();
        context.Request.Headers[GatewayCallerProof.PathHeader] =
            request.Headers.GetValues(GatewayCallerProof.PathHeader).Single();

        Assert.True(GatewayCallerProof.Validate(context, config, enforce: true));
    }

    [Fact]
    public void GatewayCallerProof_rejects_replay_for_a_different_path()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GatewayCallerProof:Secret"] = "test-secret-key" })
            .Build();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://hr-core-web:8080/api/lookup/employees");
        GatewayCallerProof.Sign(request, config);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/lookup/org-units";
        context.Request.Headers[GatewayCallerProof.ProofHeader] =
            request.Headers.GetValues(GatewayCallerProof.ProofHeader).Single();
        context.Request.Headers[GatewayCallerProof.TimestampHeader] =
            request.Headers.GetValues(GatewayCallerProof.TimestampHeader).Single();
        context.Request.Headers[GatewayCallerProof.PathHeader] =
            request.Headers.GetValues(GatewayCallerProof.PathHeader).Single();

        Assert.False(GatewayCallerProof.Validate(context, config, enforce: true));
    }
}
