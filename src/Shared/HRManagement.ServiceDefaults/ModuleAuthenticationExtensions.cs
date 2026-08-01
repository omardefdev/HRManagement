using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HRManagement.ServiceDefaults;

public static class ModuleAuthenticationExtensions
{
    /// <summary>
    /// Production: JwtBearer only (BehindGateway). Development escape hatch: Cookie+OIDC when BehindGateway=false.
    /// </summary>
    public static IServiceCollection AddModuleAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var behindGateway = configuration.GetValue("HrAuth:BehindGateway", true);

        if (!environment.IsDevelopment() && !behindGateway)
        {
            throw new InvalidOperationException(
                "HrAuth:BehindGateway must be true outside Development. Direct module OIDC is not supported in production.");
        }

        if (behindGateway)
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddKeycloakJwtBearer(configuration);
        }
        else
        {
            services.AddKeycloakAuthentication(configuration);
        }

        return services;
    }
}
