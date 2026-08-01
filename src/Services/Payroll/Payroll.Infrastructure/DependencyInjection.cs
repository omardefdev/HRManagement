using Microsoft.Extensions.DependencyInjection;
using Payroll.Application;
using Payroll.Application.Abstractions;
using Payroll.Infrastructure.Integration;
using Payroll.Infrastructure.Repositories;
using Payroll.Infrastructure.Services;
using HRManagement.Integration.FileAdapters;
using HRManagement.ServiceDefaults;

namespace Payroll.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPayrollInfrastructure(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        services.AddPayrollApplication();
        services.AddScoped<IPayrollRepository, PayrollRepository>();
        services.AddScoped<IPayrollEventPublisher, PayrollEventPublisher>();
        services.AddScoped<IEmployeeLookup, EmployeeLookupService>();
        services.AddScoped<IPayrollNationalScopeGuard, PayrollNationalScopeGuard>();
        services.AddScoped<IPaymentDisbursementGate, Services.PaymentDisbursementGate>();
        services.AddScoped<IPayrollLegalControlGuard, PayrollLegalControlGuard>();
        services.AddScoped<PayrollExternalExportService>();
        services.AddScoped<PaymentOrderService>();
        services.AddScoped<IPaymentOrderCoordinator, PaymentOrderCoordinator>();
        services.AddScoped<IBankReconciliationCoordinator, BankReconciliationCoordinator>();
        services.AddScoped<BankReconciliationService>();
        services.AddScoped<ExportJobs.IPayrollExportJobService, ExportJobs.PayrollExportJobService>();

        if (configuration is not null)
        {
            services.AddExportJobInfrastructure(configuration);
            services.Configure<Payroll.Application.Options.PayrollBatchOptions>(
                configuration.GetSection(Payroll.Application.Options.PayrollBatchOptions.SectionName));
            services.AddHrPayrollExternalIntegrations(configuration);

            var hrCoreBase = configuration["Integration:HrCoreBaseUrl"]?.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(hrCoreBase))
            {
                services.AddServiceAuthenticatedClient(
                    EmployeeLookupService.HttpClientName,
                    new Uri(hrCoreBase + "/"));
            }
            else
            {
                services.AddHttpClient(EmployeeLookupService.HttpClientName);
            }
        }
        else
        {
            services.AddHttpClient(EmployeeLookupService.HttpClientName);
            services.Configure<HRManagement.SharedKernel.Exports.ExportJobOptions>(_ => { });
            services.AddSingleton<IExportArtifactStore, LocalExportArtifactStore>();
        }

        return services;
    }
}
