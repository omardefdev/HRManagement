using HRManagement.Integration.Abstractions.Identity;
using HRManagement.Integration.Abstractions.Payroll;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Connectors;

public static class GovernmentConnectorExtensions
{
    public static IServiceCollection AddGovernmentConnectors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GovernmentConnectorOptions>(configuration.GetSection(GovernmentConnectorOptions.SectionName));

        services.AddSingleton<TreasuryLiveConnector>();
        services.AddSingleton<CivilRegistryLiveConnector>();
        services.AddSingleton<FederationConnectorDescriptor>();

        services.AddSingleton<LiveConnectorRegistry>(sp =>
        {
            var connectors = new IGovernmentConnector[]
            {
                sp.GetRequiredService<TreasuryLiveConnector>(),
                sp.GetRequiredService<CivilRegistryLiveConnector>(),
                sp.GetRequiredService<FederationConnectorDescriptor>()
            };
            return new LiveConnectorRegistry(connectors);
        });

        services.AddHostedService<GovernmentConnectorStartupValidator>();

        var opts = configuration.GetSection(GovernmentConnectorOptions.SectionName).Get<GovernmentConnectorOptions>()
                   ?? new GovernmentConnectorOptions();

        if (opts.Treasury.Mode is IntegrationConnectorMode.Live or IntegrationConnectorMode.Sandbox)
            services.AddSingleton<IPayrollDisbursementGateway>(sp => sp.GetRequiredService<TreasuryLiveConnector>());

        if (opts.CivilRegistry.Mode is IntegrationConnectorMode.Live or IntegrationConnectorMode.Sandbox)
            services.AddSingleton<INationalIdentityVerifier>(sp => sp.GetRequiredService<CivilRegistryLiveConnector>());

        return services;
    }

    public static WebApplicationBuilder AddGovernmentConnectors(this WebApplicationBuilder builder)
    {
        builder.Services.AddGovernmentConnectors(builder.Configuration);
        return builder;
    }
}
