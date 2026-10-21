using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HRManagement.ServiceDefaults;

public static class DbContextRegistrationExtensions
{
    /// <summary>
    /// Per-process cap on pooled PostgreSQL connections.
    /// <para>
    /// Npgsql defaults to 100 connections per process. Multiplied across fifteen services and
    /// their replicas that alone exceeds any sane server ceiling, and the failure mode is the
    /// worst kind: connections are refused across the estate at exactly the moment load is
    /// highest. A modest cap per process keeps the total bounded and predictable.
    /// </para>
    /// </summary>
    public const int DefaultMaxPoolSize = 25;

    public static DbContextOptionsBuilder UseHrDatabaseProvider(
        this DbContextOptionsBuilder options,
        string? connectionString,
        string migrationsAssembly)
    {
        options = options.ConfigureWarnings(w =>
            w.Log(CoreEventId.ManyServiceProvidersCreatedWarning));

        if (IsSqliteConnectionString(connectionString))
        {
            return options.UseSqlite(connectionString);
        }

        return options.UseNpgsql(
            ApplyPoolCeiling(connectionString),
            npgsql => npgsql.MigrationsAssembly(migrationsAssembly));
    }

    /// <summary>
    /// Adds <c>Maximum Pool Size</c> when the connection string does not set it, leaving any
    /// explicit operator choice untouched.
    /// </summary>
    public static string? ApplyPoolCeiling(string? connectionString, int maxPoolSize = DefaultMaxPoolSize)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (!builder.ContainsKey("Maximum Pool Size"))
                builder.MaxPoolSize = maxPoolSize;

            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            // Not a parseable Npgsql connection string; leave it for the provider to reject.
            return connectionString;
        }
    }

    private static bool IsSqliteConnectionString(string? connectionString) =>
        !string.IsNullOrWhiteSpace(connectionString)
        && connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pooled DbContext with a single EF model (avoids per-scope <c>IServiceProvider</c> churn / OOM on large models).
    /// Interceptors resolved here must be registered as singletons.
    /// </summary>
    public static IServiceCollection AddHrDbContextPool<TContext>(
        this IServiceCollection services,
        string? connectionString,
        Action<IServiceProvider, DbContextOptionsBuilder>? configure = null,
        int poolSize = 64)
        where TContext : DbContext
    {
        services.AddDbContextPool<TContext>((sp, options) =>
        {
            options.UseHrDatabaseProvider(connectionString, typeof(TContext).Assembly.GetName().Name!);
            configure?.Invoke(sp, options);
        }, poolSize);

        return services;
    }
}
