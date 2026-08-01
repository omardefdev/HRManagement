using HRManagement.Gateway.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

public class Nfr002CircuitBreakerTests
{
    [Fact]
    public void HealthProbeClient_IsRegistered_WithNamedClient()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:HealthProbe:CircuitBreakFailures"] = "2",
                ["Gateway:HealthProbe:CircuitBreakSeconds"] = "5"
            })
            .Build();
        services.AddGatewayHealthProbeClient(config);
        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(GatewayHttpResilience.HealthProbeClientName);
        Assert.NotNull(client);
    }
}
