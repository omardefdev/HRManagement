using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

public class GatewayAuthenticationTests
{
    [Fact]
    public async Task AddGatewayKeycloakAuthentication_registers_jwt_bearer_and_policy_scheme()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                ["Keycloak:ClientId"] = "hr-web",
                ["Keycloak:ClientSecret"] = "secret"
            })
            .Build();

        services.AddLogging();
        services.AddGatewayKeycloakAuthentication(config);

        using var provider = services.BuildServiceProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var defaultScheme = await schemeProvider.GetDefaultAuthenticateSchemeAsync();
        Assert.Equal(KeycloakExtensions.GatewayAuthenticationScheme, defaultScheme?.Name);

        var challengeScheme = await schemeProvider.GetDefaultChallengeSchemeAsync();
        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, challengeScheme?.Name);

        var jwtScheme = await schemeProvider.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme);
        Assert.NotNull(jwtScheme);

        var oidcScheme = await schemeProvider.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.NotNull(oidcScheme);
    }
}
