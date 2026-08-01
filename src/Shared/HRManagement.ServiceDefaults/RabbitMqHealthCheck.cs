using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace HRManagement.ServiceDefaults;

public sealed class RabbitMqHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("HealthChecks:IncludeRabbitMq", true))
            return HealthCheckResult.Healthy("RabbitMQ check skipped.");

        try
        {
            var host = configuration["RabbitMQ:Host"] ?? "localhost";
            var port = configuration.GetValue("RabbitMQ:Port", (ushort)5672);
            var virtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/";
            var username = configuration["RabbitMQ:Username"]
                ?? throw new InvalidOperationException("RabbitMQ:Username is required");
            var password = configuration["RabbitMQ:Password"]
                ?? throw new InvalidOperationException("RabbitMQ:Password is required");

            var factory = new ConnectionFactory
            {
                HostName = host,
                Port = port,
                VirtualHost = virtualHost,
                UserName = username,
                Password = password,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
            };

            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            return connection.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Connection not open.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ unreachable.", ex);
        }
    }
}
