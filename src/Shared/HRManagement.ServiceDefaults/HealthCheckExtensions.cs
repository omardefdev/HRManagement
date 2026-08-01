using System.Text.Json;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Builder;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Routing;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Microsoft.Extensions.Hosting;



namespace HRManagement.ServiceDefaults;



public static class HealthCheckExtensions

{

    /// <summary>

    /// Registers PostgreSQL (via EF DbContext) and optional RabbitMQ readiness checks.

    /// </summary>

    public static IServiceCollection AddHrServiceHealthChecks<TDbContext>(

        this IServiceCollection services,

        IConfiguration configuration,

        bool includeRabbitMq = true)

        where TDbContext : DbContext

    {

        if (includeRabbitMq)

            services.AddSingleton<RabbitMqHealthCheck>();



        var builder = services.AddHealthChecks()

            .AddDbContextCheck<TDbContext>("database", tags: ["db", "ready"]);



        if (includeRabbitMq)

            builder.AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["mq", "ready"]);



        return services;

    }



    /// <summary>

    /// Maps /health (all), /health/live (liveness), and /health/ready (readiness) endpoints.

    /// In non-Development environments, /health and /health/ready return status only (no check details).

    /// </summary>

    public static IEndpointRouteBuilder MapHrHealthChecks(this IEndpointRouteBuilder endpoints)

    {

        var env = endpoints.ServiceProvider.GetService<IHostEnvironment>();

        var redactDetails = env is null || !env.IsDevelopment();



        if (redactDetails)

        {

            endpoints.MapHealthChecks("/health", new HealthCheckOptions

            {

                ResponseWriter = WriteStatusOnlyAsync

            }).AllowAnonymous();

            endpoints.MapHealthChecks("/health/live", new HealthCheckOptions

            {

                Predicate = _ => false

            }).AllowAnonymous();

            endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions

            {

                Predicate = check => check.Tags.Contains("ready"),

                ResponseWriter = WriteStatusOnlyAsync

            }).AllowAnonymous();

        }

        else

        {

            endpoints.MapHealthChecks("/health").AllowAnonymous();

            endpoints.MapHealthChecks("/health/live", new HealthCheckOptions

            {

                Predicate = _ => false

            }).AllowAnonymous();

            endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions

            {

                Predicate = check => check.Tags.Contains("ready")

            }).AllowAnonymous();

        }



        return endpoints;

    }



    private static Task WriteStatusOnlyAsync(HttpContext context, HealthReport report)

    {

        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = JsonSerializer.Serialize(new { status = report.Status.ToString() });

        return context.Response.WriteAsync(payload);

    }

}


