using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace HRManagement.UnitTests;

public class ModuleAuthenticationExtensionsTests
{
    [Fact]
    public async Task BehindGateway_in_production_registers_jwt_bearer()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HrAuth:BehindGateway"] = "true",
                ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement"
            })
            .Build();

        var env = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Production);
        services.AddLogging();
        services.AddModuleAuthentication(config, env);

        using var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetDefaultAuthenticateSchemeAsync();

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, scheme?.Name);
    }

    [Fact]
    public void Standalone_outside_development_throws()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HrAuth:BehindGateway"] = "false",
                ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                ["Keycloak:ClientId"] = "hr-web"
            })
            .Build();

        var env = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Production);

        Assert.Throws<InvalidOperationException>(() =>
            services.AddModuleAuthentication(config, env));
    }
}
