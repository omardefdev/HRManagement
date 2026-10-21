using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HRManagement.IntegrationTests;

public class HRCoreJwtBearerIntegrationTests : IClassFixture<WebApplicationFactory<HRCoreWebProgram>>
{
    private readonly WebApplicationFactory<HRCoreWebProgram> _factory;

    public HRCoreJwtBearerIntegrationTests(WebApplicationFactory<HRCoreWebProgram> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recruitment:Jobs:IntakeCloseEnabled", "false"); // HCM-026: no background intake job in tests
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HrAuth:BehindGateway"] = "true",
                    ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                    ["HrGateway:InternalBaseUrl"] = "http://localhost:5200",
                    ["ConnectionStrings:HRCoreDb"] = "Host=invalid;Database=test",
                    ["Database:RunMigrationsAtStartup"] = "false"
                });
            });
        });
    }

    [Fact]
    public async Task Protected_endpoint_without_bearer_returns_unauthorized()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Employees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_with_invalid_bearer_returns_unauthorized()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.token.value");

        var response = await client.GetAsync("/Employees");

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }
}
