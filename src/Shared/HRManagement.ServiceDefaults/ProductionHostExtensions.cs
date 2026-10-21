using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HRManagement.ServiceDefaults;

public static class ProductionHostExtensions
{
    /// <summary>Gateway YARP prefix for the anonymous citizen careers portal (distinct from /ui/hr-core).</summary>
    public const string PublicCareersGatewayPrefix = "/careers";
    /// <summary>
    /// Registers forwarded headers (reverse proxy), optional CORS, and graceful shutdown timeout (OPS-001).
    /// Call <see cref="UseHrProductionMiddleware"/> early in the pipeline when enabled.
    /// </summary>
    public static WebApplicationBuilder AddHrProductionNetworking(this WebApplicationBuilder builder)
    {
        // Prefer Host:ShutdownTimeoutSeconds; ASPNETCORE_SHUTDOWNTIMEOUTSECONDS is also honored by the host.
        var shutdownSeconds = builder.Configuration.GetValue("Host:ShutdownTimeoutSeconds", 30);
        if (shutdownSeconds < 5)
            shutdownSeconds = 5;
        if (shutdownSeconds > 120)
            shutdownSeconds = 120;

        builder.Services.Configure<HostOptions>(options =>
            options.ShutdownTimeout = TimeSpan.FromSeconds(shutdownSeconds));

        var useForwarded = builder.Configuration.GetValue("UseForwardedHeaders", false);
        if (useForwarded)
        {
            var trustedProxies = ForwardedProxyTrust.Resolve(builder.Configuration);
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                    | ForwardedHeaders.XForwardedProto
                    | ForwardedHeaders.XForwardedPrefix;
                options.ForwardLimit = trustedProxies.ForwardLimit;

                // Empty KnownProxies *and* KnownIPNetworks means "trust every sender", which lets
                // any client set X-Forwarded-For and become whatever IP it likes. Rate limiting,
                // the /metrics allow-list, and the loopback bypass on upstream health all key off
                // RemoteIpAddress, so they would all be defeated at once.
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var proxy in trustedProxies.Proxies)
                    options.KnownProxies.Add(proxy);
                foreach (var network in trustedProxies.Networks)
                    options.KnownIPNetworks.Add(network);
            });
        }

        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (origins is { Length: > 0 })
        {
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(
                    "HrManagement",
                    p => p.WithOrigins(origins)
                        .AllowAnyHeader()
                        .AllowAnyMethod());
            });
        }

        builder.Services.AddHrSecurityHeaders(builder.Configuration);

        return builder;
    }

    public static WebApplication UseHrProductionMiddleware(this WebApplication app, IConfiguration configuration)
    {
        if (configuration.GetValue("UseForwardedHeaders", false))
        {
            app.UseHrConnectionOrigin();
            app.UseForwardedHeaders();
        }

        // Validate gateway proof on the raw proxied path before PathBase rewriting (/careers/*, /ui/*).
        app.UseHrGatewayCallerProof();
        app.UseHrConfiguredPathBase(configuration);

        if (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() is { Length: > 0 })
            app.UseCors("HrManagement");

        app.UseHrConcurrencyConflictHandling();
        app.UseHrSecurityHeaders();

        return app;
    }

    /// <summary>
    /// Applies the public module path base even when the reverse proxy forwards only
    /// the catch-all remainder. This keeps generated MVC links and redirects under
    /// paths such as /ui/hr-core instead of leaking them to the gateway root.
    /// </summary>
    public static IApplicationBuilder UseHrConfiguredPathBase(
        this IApplicationBuilder app,
        IConfiguration configuration)
    {
        var configuredValue = configuration["ASPNETCORE_PATHBASE"]
                              ?? Environment.GetEnvironmentVariable("ASPNETCORE_PATHBASE");
        if (string.IsNullOrWhiteSpace(configuredValue))
            return app;

        var normalizedValue = configuredValue.Trim();
        if (!normalizedValue.StartsWith('/'))
            normalizedValue = "/" + normalizedValue;
        normalizedValue = normalizedValue.TrimEnd('/');
        if (normalizedValue.Length == 0)
            return app;

        var configuredPathBase = new PathString(normalizedValue);
        var publicCareersPathBase = new PathString(PublicCareersGatewayPrefix);
        return app.Use(async (context, next) =>
        {
            var originalPathBase = context.Request.PathBase;
            var originalPath = context.Request.Path;

            // /careers is proxied anonymously at the gateway root. Do not stamp /ui/{module}
            // or generated links and static assets will require HR login.
            if (!context.Request.PathBase.HasValue
                && context.Request.Path.StartsWithSegments(publicCareersPathBase, out var careersRemainder))
            {
                context.Request.PathBase = publicCareersPathBase;
                context.Request.Path = careersRemainder.HasValue ? careersRemainder : new PathString("/");
            }
            else if (context.Request.Path.StartsWithSegments(configuredPathBase, out var remainingPath))
            {
                if (!context.Request.PathBase.HasValue)
                    context.Request.PathBase = configuredPathBase;
                context.Request.Path = remainingPath;
            }
            else if (!context.Request.PathBase.HasValue)
            {
                context.Request.PathBase = configuredPathBase;
            }

            try
            {
                await next(context);
            }
            finally
            {
                context.Request.PathBase = originalPathBase;
                context.Request.Path = originalPath;
            }
        });
    }
}
