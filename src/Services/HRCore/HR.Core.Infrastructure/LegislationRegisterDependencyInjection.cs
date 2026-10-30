using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.Legal;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Core.Infrastructure;

public static class LegislationRegisterDependencyInjection
{
    public static IServiceCollection AddHrLegislationRegister(this IServiceCollection services)
    {
        services.AddScoped<ILegislationRegisterProvider, LegislationRegisterProvider>();
        services.AddScoped<RecruitmentLegalSourceValidator>();
        return services;
    }

    public static IServiceCollection AddHrServiceRegimes(this IServiceCollection services)
    {
        services.AddScoped<IServiceRegimeProvider, ServiceRegimeProvider>();
        services.AddScoped<ServiceRegimeValidator>();
        services.AddScoped<ServiceRegimeTransitionService>();
        services.AddScoped<ServiceRegimeExceptionService>();
        services.AddScoped<ServiceRegimeBackfillService>();
        services.AddScoped<ServiceRegimePayrollBlockService>();
        services.AddScoped<ServiceRegimeActiveRegimeService>();
        return services;
    }
}
