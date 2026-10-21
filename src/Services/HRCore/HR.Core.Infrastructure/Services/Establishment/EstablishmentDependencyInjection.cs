using Microsoft.Extensions.DependencyInjection;

namespace HR.Core.Infrastructure.Services.Establishment;

public static class EstablishmentDependencyInjection
{
    public static IServiceCollection AddHrEstablishmentTemporal(this IServiceCollection services)
    {
        services.AddScoped<IJobCatalogVersionService, JobCatalogVersionService>();
        services.AddScoped<IFundedPositionVersionService, FundedPositionVersionService>();
        services.AddScoped<EstablishmentAsOfService>();
        return services;
    }

    public static IServiceCollection AddHrEstablishmentPlanning(this IServiceCollection services)
    {
        services.AddScoped<IEstablishmentMetricsService, EstablishmentMetricsService>();
        services.AddScoped<IEstablishmentGapAnalysisService, EstablishmentGapAnalysisService>();
        services.AddScoped<IWorkforcePlanService, WorkforcePlanService>();
        services.AddScoped<IFinanceBudgetPort, FileFinanceBudgetPort>();
        services.AddScoped<IFinanceBudgetImportService, FinanceBudgetImportService>();
        services.AddScoped<IJobCatalogImportPort, FileJobCatalogPort>();
        services.AddScoped<IJobCatalogImportService, JobCatalogImportService>();
        services.AddScoped<IEstablishmentFinanceReconciliationService, EstablishmentFinanceReconciliationService>();
        return services;
    }
}
