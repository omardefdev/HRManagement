using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Attendance;
using HRManagement.Integration.Abstractions.Identity;
using HRManagement.Integration.Abstractions.Insurance;
using HRManagement.Integration.Abstractions.Payroll;
using HRManagement.Integration.Abstractions.Tax;
using HRManagement.Integration.FileAdapters.Attendance;
using HRManagement.Integration.FileAdapters.Identity;
using HRManagement.Integration.FileAdapters.Insurance;
using HRManagement.Integration.FileAdapters.Payroll;
using HRManagement.Integration.FileAdapters.Tax;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.Integration.FileAdapters;

public static class ExternalIntegrationServiceCollectionExtensions
{
    /// <summary>يسجّل موصّلات Payroll (المالية + التأمينات + الضريبة).</summary>
    public static IServiceCollection AddHrPayrollExternalIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExternalIntegrationsOptions>(configuration.GetSection(ExternalIntegrationsOptions.SectionName));
        var mode = configuration.GetSection(ExternalIntegrationsOptions.SectionName).GetValue<IntegrationMode?>("Mode")
                   ?? IntegrationMode.File;

        services.AddSingleton<IPayrollDisbursementGateway, FilePayrollDisbursementGateway>();
        services.AddSingleton<ISocialInsuranceReporter, FileSocialInsuranceReporter>();

        if (mode == IntegrationMode.Stub)
            services.AddSingleton<ITaxWithholdingReporter, StubTaxWithholdingReporter>();
        else
            services.AddSingleton<ITaxWithholdingReporter, FileTaxWithholdingReporter>();

        return services;
    }

    /// <summary>يسجّل موصّلات HR.Core (الهوية + البصمة).</summary>
    public static IServiceCollection AddHrCoreExternalIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExternalIntegrationsOptions>(configuration.GetSection(ExternalIntegrationsOptions.SectionName));
        var mode = configuration.GetSection(ExternalIntegrationsOptions.SectionName).GetValue<IntegrationMode?>("Mode")
                   ?? IntegrationMode.File;

        if (mode == IntegrationMode.Stub)
        {
            services.AddSingleton<INationalIdentityVerifier, StubNationalIdentityVerifier>();
            services.AddSingleton<IBiometricAttendanceProvider, StubBiometricAttendanceProvider>();
        }
        else
        {
            services.AddSingleton<INationalIdentityVerifier, FileNationalIdentityVerifier>();
            services.AddSingleton<IBiometricAttendanceProvider, FileBiometricAttendanceProvider>();
        }

        return services;
    }
}
