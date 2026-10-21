using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

public class ProductionHostPathBaseTests
{
    [Theory]
    [InlineData("/Employees", "/ui/hr-core", "/Employees")]
    [InlineData("/ui/hr-core/Employees", "/ui/hr-core", "/Employees")]
    [InlineData("/ui/hr-core", "/ui/hr-core", "")]
    [InlineData("/careers", "/careers", "/")]
    [InlineData("/careers/track", "/careers", "/track")]
    [InlineData("/careers/lib/bootstrap/css/bootstrap.rtl.min.css", "/careers", "/lib/bootstrap/css/bootstrap.rtl.min.css")]
    public async Task Configured_path_base_supports_proxy_stripped_and_full_paths(
        string requestPath,
        string expectedPathBase,
        string expectedPath)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_PATHBASE"] = "/ui/hr-core"
            })
            .Build();
        var builder = new ApplicationBuilder(services);
        PathString observedPathBase = default;
        PathString observedPath = default;

        builder.UseHrConfiguredPathBase(configuration);
        builder.Run(context =>
        {
            observedPathBase = context.Request.PathBase;
            observedPath = context.Request.Path;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = requestPath;
        await builder.Build()(context);

        Assert.Equal(expectedPathBase, observedPathBase.Value);
        Assert.Equal(expectedPath, observedPath.Value);
        Assert.Equal(PathString.Empty, context.Request.PathBase);
        Assert.Equal(requestPath, context.Request.Path.Value);
    }

    [Fact]
    public async Task Existing_forwarded_path_base_remains_authoritative()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_PATHBASE"] = "/ui/hr-core"
            })
            .Build();
        var builder = new ApplicationBuilder(services);
        PathString observedPathBase = default;

        builder.UseHrConfiguredPathBase(configuration);
        builder.Run(context =>
        {
            observedPathBase = context.Request.PathBase;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.PathBase = "/edge/ui/hr-core";
        context.Request.Path = "/Employees";
        await builder.Build()(context);

        Assert.Equal("/edge/ui/hr-core", observedPathBase.Value);
        Assert.Equal("/edge/ui/hr-core", context.Request.PathBase.Value);
    }
}
