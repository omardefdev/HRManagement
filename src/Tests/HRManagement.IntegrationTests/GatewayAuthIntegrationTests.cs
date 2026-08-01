using System.Net;
using HRManagement.Gateway.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace HRManagement.IntegrationTests;

public sealed class GatewayTestFactory : WebApplicationFactory<GatewayWebProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("Database:RunMigrationsAtStartup", "false");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GatewayAuditDb"] = "Host=127.0.0.1;Port=1;Database=gateway-smoke",
                ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                ["Keycloak:ClientId"] = "hr-gateway-client",
                ["Keycloak:ClientSecret"] = "test-secret",
                ["HrGateway:InternalBaseUrl"] = "http://localhost:5080"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<GatewayAuditDbContext>>();
            services.RemoveAll<GatewayAuditDbContext>();
            services.AddDbContext<GatewayAuditDbContext>(o => o.UseInMemoryDatabase("gateway-smoke"));

            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(descriptor);

            services.PostConfigure<HealthCheckServiceOptions>(options => options.Registrations.Clear());
        });
    }
}

public class GatewayAuthIntegrationTests : IClassFixture<GatewayTestFactory>
{
    private readonly GatewayTestFactory _factory;

    public GatewayAuthIntegrationTests(GatewayTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_live_endpoint_responds()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
