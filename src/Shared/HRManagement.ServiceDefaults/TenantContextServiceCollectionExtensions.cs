using HRManagement.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public static class TenantContextServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ITenantContext"/> and tenant EF interceptors (TEN-001/002).</summary>
    public static IServiceCollection AddTenantContext(this IServiceCollection services)
    {
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<TenantIdStampInterceptor>();
        services.AddScoped<NpgsqlTenantSessionInterceptor>();
        return services;
    }
}
