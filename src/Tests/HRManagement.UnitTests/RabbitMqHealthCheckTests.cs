using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HRManagement.UnitTests;

public class RabbitMqHealthCheckTests
{
    [Fact]
    public async Task Returns_Healthy_when_check_is_skipped()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HealthChecks:IncludeRabbitMq"] = "false"
            })
            .Build();

        var check = new RabbitMqHealthCheck(config);
        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("rabbitmq", check, null, null)
            });

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("skipped", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_Unhealthy_when_connection_fails()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HealthChecks:IncludeRabbitMq"] = "true",
                ["RabbitMQ:Host"] = "localhost",
                ["RabbitMQ:Port"] = "59999"
            })
            .Build();

        var check = new RabbitMqHealthCheck(config);
        var result = await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("rabbitmq", check, null, null)
            });

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
