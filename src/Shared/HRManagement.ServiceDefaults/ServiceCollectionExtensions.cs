using HRManagement.ServiceDefaults.Repositories;
using HRManagement.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddServiceDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName) where TContext : DbContext
    {
        var maxPoolSize = configuration.GetValue(
            "Database:MaxPoolSize",
            DbContextRegistrationExtensions.DefaultMaxPoolSize);

        services.AddDbContext<TContext>(options =>
            options.UseNpgsql(
                DbContextRegistrationExtensions.ApplyPoolCeiling(
                    configuration.GetConnectionString(connectionStringName),
                    maxPoolSize),
                npgsql => npgsql.MigrationsAssembly(typeof(TContext).Assembly.FullName)));

        return services;
    }

    public static IServiceCollection AddRepositories<TContext>(
        this IServiceCollection services) where TContext : DbContext
    {
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
        return services;
    }
}
