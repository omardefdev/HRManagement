using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // PLAN-53: the salary change transaction. The clock is injected so the month rules are testable (P-6).
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPayrollCalendar, PayrollCalendar>();
        services.AddOptions<Options.PayProfileChangeOptions>().BindConfiguration(Options.PayProfileChangeOptions.SectionName);
        services.AddOptions<Features.ManualInputs.PayrollManualInputOptions>().BindConfiguration(Features.ManualInputs.PayrollManualInputOptions.SectionName);
        services.AddOptions<Options.PayrollMasterDataOptions>().BindConfiguration(Options.PayrollMasterDataOptions.SectionName);
        services.AddScoped<Features.PayProfileChanges.PayProfileChangeRules>();
        services.AddScoped<Features.PayProfileChanges.IPayProfileChangeApplier, Features.PayProfileChanges.PayProfileChangeApplier>();
        services.AddScoped<Features.PayProfileChanges.PayProfileChangeActivator>();
        services.AddScoped<Features.ReferenceChanges.PayrollReferenceChangeApplier>();

        // PLAN-55: the employee's bank account (registered by one person, verified by another), and the banks whose local numbers count.
        services.AddOptions<Features.BankAccounts.PayrollBankOptions>().BindConfiguration(Features.BankAccounts.PayrollBankOptions.SectionName);
        services.AddScoped<Features.BankAccounts.BankAccountRules>();

        return services;
    }
}
