using HRManagement.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public static class TenantContextServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ITenantContext"/> and tenant EF interceptors (TEN-001/002).</summary>
    public static IServiceCollection AddTenantContext(this IServiceCollection services)
    {
        // Singleton: resolves tenant per request via IHttpContextAccessor / TenantIdFlow (safe with DbContext pooling).
        services.AddMemoryCache();
        services.AddSingleton<ITenantContext, TenantContext>();
        services.AddSingleton<IPlatformTenantSessionStore, MemoryPlatformTenantSessionStore>();
        services.AddSingleton<TenantIdStampInterceptor>();
        services.AddSingleton<NpgsqlTenantSessionInterceptor>();
        return services;
    }
}
