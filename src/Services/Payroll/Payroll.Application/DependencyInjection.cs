using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
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

        // PLAN-57: the attendance gate before a regular run's calculation (Payroll:Attendance:Gate, Warn by default).
        services.AddOptions<Options.PayrollAttendanceOptions>()
            .Configure<IServiceProvider>((o, sp) =>
                sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>()?.GetSection(Options.PayrollAttendanceOptions.SectionName).Bind(o));
        services.AddScoped<PayrollAttendanceGate>();

        services.AddScoped<FraudAnomalyEngine>();
        services.AddScoped<PaymentHoldService>();

        // PLAN-58: disbursement governance — the agency's settings (Payroll:Approval, :Audit, :Blockers, :Sod, :DualControl), the one
        // maker of a payment batch, and the separation of duties with the agency's double-approval threshold (D-PAY-5).
        services.AddOptions<Options.PayrollApprovalOptions>()
            .Configure<IServiceProvider>((o, sp) => sp.GetService<IConfiguration>()?.GetSection(Options.PayrollApprovalOptions.SectionName).Bind(o));
        services.AddOptions<Options.PayrollAuditOptions>()
            .Configure<IServiceProvider>((o, sp) => sp.GetService<IConfiguration>()?.GetSection(Options.PayrollAuditOptions.SectionName).Bind(o));
        services.AddOptions<Options.PayrollBlockerOptions>()
            .Configure<IServiceProvider>((o, sp) => sp.GetService<IConfiguration>()?.GetSection(Options.PayrollBlockerOptions.SectionName).Bind(o));
        services.AddOptions<Options.PayrollSodOptions>()
            .Configure<IServiceProvider>((o, sp) => sp.GetService<IConfiguration>()?.GetSection(Options.PayrollSodOptions.SectionName).Bind(o));
        services.AddOptions<Options.PayrollDualControlOptions>()
            .Configure<IServiceProvider>((o, sp) => sp.GetService<IConfiguration>()?.GetSection(Options.PayrollDualControlOptions.SectionName).Bind(o));
        services.AddSingleton(sp => Options.PayrollSeparationOfDuties.Create(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Options.PayrollApprovalOptions>>().Value.DualApprovalThreshold));
        services.AddScoped<PaymentBatchIssuer>();

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
        services.AddOptions<Features.BankAccounts.PayrollBankOptions>()
            .BindConfiguration(Features.BankAccounts.PayrollBankOptions.SectionName)
            // A bank whose pattern does not compile would refuse every number of that bank without a word: the service names it at start.
            .Validate(options => options.Problems().Count == 0, "Payroll:Bank:Banks is not usable: a bank needs a code, an Arabic name and an account pattern that compiles, and a code is defined once.")
            .ValidateOnStart();
        services.AddScoped<Features.BankAccounts.BankAccountRules>();

        return services;
    }
}
