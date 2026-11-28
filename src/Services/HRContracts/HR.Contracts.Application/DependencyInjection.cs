using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HR.Contracts.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddHrContractsApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        // PLAN-60: the one creation path of a separation, and the Separation section (defaults when it is absent).
        services.AddOptions<Features.SeparationRecords.SeparationOptions>();
        services.AddScoped<Features.SeparationRecords.SeparationRecordFactory>();
        services.AddScoped<Features.SeparationRecords.SeparationApprovalService>();
        // PLAN-60 (§5): Damascus's «today». The web host's AddHrLocalization registers the calendar first; a test host gets the default.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<HRManagement.ServiceDefaults.Localization.ICalendarService>(
            _ => new HRManagement.ServiceDefaults.Localization.DamascusCalendarService());
        services.TryAddSingleton<Features.SeparationRecords.SeparationClock>();
        return services;
    }
}
