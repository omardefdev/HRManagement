using HR.Core.Infrastructure.Services.Establishment;
using Microsoft.Extensions.DependencyInjection;
using HR.Core.Infrastructure.Services.OrgStructure;

namespace HR.Core.Infrastructure;

public static class OrgStructureDependencyInjection
{
    public static IServiceCollection AddHrOrgStructure(this IServiceCollection services)
    {
        services.AddScoped<IOrgUnitVersionService, OrgUnitVersionService>();
        return services;
    }
}
