using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Contracts.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddHrContractsApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }
}
