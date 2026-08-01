using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.ValueKpi;

public static class ValueKpiExtensions
{
    public static IServiceCollection AddValueKpi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ValueKpiOptions>(configuration.GetSection(ValueKpiOptions.SectionName));
        services.AddSingleton<IValueKpiProvider, ValueKpiProvider>();
        services.AddSingleton<ValueKpiMetrics>();
        services.AddSingleton<IValueBenefitGuard, ValueBenefitGuard>();
        services.AddHostedService<ValueKpiStartupValidator>();
        return services;
    }

    public static WebApplicationBuilder AddValueKpi(this WebApplicationBuilder builder)
    {
        builder.Services.AddValueKpi(builder.Configuration);
        return builder;
    }
}
