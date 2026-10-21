using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using HRManagement.SharedKernel.SeparationOfDuties;
using Payroll.Application.Frd;
using Payroll.Application.Services;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;

namespace Payroll.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPayrollApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddSingleton<IPayrollEngine>(PayrollEngine.CreateDefaultPipeline());
        services.AddOptions<Payroll.Application.Options.PayrollBatchOptions>();
        services.AddOptions<FrdAnomalyRulesOptions>().BindConfiguration(FrdAnomalyRulesOptions.SectionName);
        services.AddSingleton<IFrdAnomalyRulesProvider, FrdAnomalyRulesProvider>();
        services.AddScoped<PartialDisbursementService>();
        services.AddScoped<ArrearsSettlementService>();

        services.AddScoped<FraudAnomalyEngine>();
        services.AddScoped<PaymentHoldService>();
        services.AddSingleton<SeparationOfDutiesService>();

        return services;
    }
}
