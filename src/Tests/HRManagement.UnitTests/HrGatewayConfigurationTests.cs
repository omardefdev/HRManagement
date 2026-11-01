using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

[Collection(ProcessEnvironmentCollection.Name)]
public class HrGatewayConfigurationTests
{
    [Fact]
    public void RequireInternalBaseUrl_returns_trimmed_url()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [HrGatewayConfigurationExtensions.InternalBaseUrlKey] = "http://hr-gateway:8080/"
            })
            .Build();

        var url = HrGatewayConfigurationExtensions.RequireInternalBaseUrl(configuration);

        Assert.Equal("http://hr-gateway:8080", url);
    }

    [Fact]
    public void RequireInternalBaseUrl_throws_when_missing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            HrGatewayConfigurationExtensions.RequireInternalBaseUrl(configuration));

        Assert.Contains("HrGateway:InternalBaseUrl", ex.Message);
    }

    [Fact]
    public void RequireInternalBaseUrl_throws_when_localhost_inside_container()
    {
        var previous = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [HrGatewayConfigurationExtensions.InternalBaseUrlKey] = "http://localhost:5200"
                })
                .Build();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                HrGatewayConfigurationExtensions.RequireInternalBaseUrl(configuration));

            Assert.Contains("hr-gateway:8080", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", previous);
        }
    }

    [Fact]
    public void AddHrFileReferenceClient_throws_when_internal_base_url_missing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            FileReferenceClientExtensions.AddHrFileReferenceClient(services, configuration));

        Assert.Contains("HrGateway:InternalBaseUrl", ex.Message);
    }
}
