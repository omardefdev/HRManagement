using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

public static class MassTransitExtensions
{
    public static IServiceCollection AddServiceMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null,
        Action<IBusRegistrationConfigurator>? configureOutbox = null,
        bool enableInboxFilter = true,
        Assembly? consumerAssembly = null)
    {
        services.AddSingleton<MassTransitFaultLogger>();
        services.AddSingleton<IConsumeObserver>(sp => sp.GetRequiredService<MassTransitFaultLogger>());
        services.AddSingleton<InboxMetrics>();
        services.AddSingleton<SagaMetrics>();
        services.Configure<InboxOptions>(configuration.GetSection(InboxOptions.SectionName));
        services.AddHostedService<InboxRetentionHostedService>();

        if (consumerAssembly is not null)
        {
            var opts = new ConsumerRegistrationOptions { FailOnUnregistered = true };
            opts.AssembliesToScan.Add(consumerAssembly);
            foreach (var t in ConsumerRegistrationHostedService.DiscoverConsumers(consumerAssembly))
                opts.RegisteredConsumerTypes.Add(t);
            services.AddSingleton(opts);
            services.AddHostedService<ConsumerRegistrationHostedService>();
        }

        services.AddMassTransit(x =>
        {
            var endpointPrefix = ResolveEndpointPrefix(configuration);
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(endpointPrefix, includeNamespace: false));

            if (consumerAssembly is not null)
                x.AddConsumers(consumerAssembly);

            configureConsumers?.Invoke(x);
            configureOutbox?.Invoke(x);

            x.AddConfigureEndpointsCallback((_, name, cfg) =>
            {
                // OPS-002: quorum queues for MassTransit receive endpoints (safe on single-node Compose too).
                if (cfg is IRabbitMqReceiveEndpointConfigurator rabbit
                    && configuration.GetValue("RabbitMQ:UseQuorumQueues", true))
                {
                    rabbit.SetQuorumQueue();
                }
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(
                    configuration.GetValue<string>("RabbitMQ:Host") ?? "localhost",
                    configuration.GetValue<ushort>("RabbitMQ:Port"),
                    configuration.GetValue<string>("RabbitMQ:VirtualHost") ?? "/",
                    h =>
                    {
                        h.Username(configuration["RabbitMQ:Username"]
                            ?? throw new InvalidOperationException("RabbitMQ:Username is required"));
                        h.Password(configuration["RabbitMQ:Password"]
                            ?? throw new InvalidOperationException("RabbitMQ:Password is required"));
                        // OPS-002: publisher confirms — do not consider publish durable until broker ack.
                        h.PublisherConfirmation = configuration.GetValue("RabbitMQ:PublisherConfirmation", true);
                    });

                ConfigureRetryPolicy(cfg, configuration);

                cfg.UseDelayedMessageScheduler();
                cfg.UseConsumeFilter(typeof(CorrelationIdConsumeFilter<>), context);
                cfg.UseConsumeFilter(typeof(TenantIdConsumeFilter<>), context);
                if (enableInboxFilter)
                    cfg.UseConsumeFilter(typeof(InboxConsumeFilter<>), context);

                cfg.ConfigurePublish(pub => pub.UseExecute(ctx =>
                {
                    ApplyOutboundCorrelation(ctx);
                    ApplyOutboundTenantId(ctx);
                }));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    private static string ResolveEndpointPrefix(IConfiguration configuration)
    {
        var configured = configuration["MassTransit:EndpointPrefix"];
        var applicationName = string.IsNullOrWhiteSpace(configured)
            ? Assembly.GetEntryAssembly()?.GetName().Name ?? "hrmanagement"
            : configured;
        return Regex.Replace(applicationName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }

    public static void AddConsumersFromAssembly(
        this IBusRegistrationConfigurator configurator,
        Assembly assembly) =>
        configurator.AddConsumers(assembly);

    public static void AddPostgresOutbox<TDbContext>(this IBusRegistrationConfigurator configurator)
        where TDbContext : DbContext
    {
        configurator.AddEntityFrameworkOutbox<TDbContext>(o =>
        {
            o.QueryDelay = TimeSpan.FromSeconds(1);
            o.UsePostgres();
            o.UseBusOutbox();
        });
    }

    private static void ConfigureRetryPolicy(
        IRabbitMqBusFactoryConfigurator cfg,
        IConfiguration configuration)
    {
        if (configuration.GetValue("MassTransit:RetryCount", 3) <= 0)
            return;

        cfg.UseMessageRetry(r => ConfigureMessageRetry(r, configuration));
    }

    /// <summary>
    /// The message retry policy of every service bus (also used by the MassTransit test harness, PLAN-37): transient
    /// faults are retried, permanent business/validation faults go straight to <c>*_error</c>.
    /// </summary>
    public static void ConfigureMessageRetry(IRetryConfigurator r, IConfiguration configuration)
    {
        var retryCount = configuration.GetValue("MassTransit:RetryCount", 3);
        var retryIntervalSeconds = Math.Max(1, configuration.GetValue("MassTransit:RetryIntervalSeconds", 5));

        // NFR-002: prefer exponential intervals with jitter (MassTransit Exponential uses randomized delays).
        // Set MassTransit:RetryUseJitter=false to restore fixed Interval for lab A/B.
        var useJitter = configuration.GetValue("MassTransit:RetryUseJitter", true);

        if (useJitter)
        {
            var min = TimeSpan.FromMilliseconds(retryIntervalSeconds * 500d);
            var max = TimeSpan.FromSeconds(retryIntervalSeconds * Math.Max(4, retryCount));
            var delta = TimeSpan.FromSeconds(retryIntervalSeconds);
            r.Exponential(retryCount, min, max, delta);
        }
        else
        {
            r.Interval(retryCount, TimeSpan.FromSeconds(retryIntervalSeconds));
        }

        // Permanent business/validation faults → *_error. Transient → retry.
        r.Ignore<InvalidOperationException>();
        r.Ignore<ArgumentException>();
        r.Ignore<ArgumentNullException>();
        r.Ignore<UnauthorizedAccessException>();
    }

    private static void ApplyOutboundCorrelation(PublishContext context)
    {
        var id = CorrelationIdFlow.Get() ?? Activity.Current?.Id;
        if (string.IsNullOrWhiteSpace(id))
            return;
        if (!context.Headers.TryGetHeader(CorrelationIdMiddleware.HeaderName, out _))
            context.Headers.Set(CorrelationIdMiddleware.HeaderName, id);
    }

    private static void ApplyOutboundTenantId(PublishContext context)
    {
        var tenantId = TenantIdFlow.Get();
        if (!tenantId.HasValue)
            return;
        if (!context.Headers.TryGetHeader(TenantIdMiddleware.HeaderName, out _))
            context.Headers.Set(TenantIdMiddleware.HeaderName, tenantId.Value.ToString("D"));
    }
}

public sealed class InboxOptions
{
    public const string SectionName = "Inbox";
    public int RetentionDays { get; set; } = 90;
    public bool EnableRetention { get; set; } = true;
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(6);
}

/// <summary>INT-002: deletes inbox receipts older than retention window.</summary>
public sealed class InboxRetentionHostedService(
    IServiceScopeFactory scopeFactory,
    Microsoft.Extensions.Options.IOptions<InboxOptions> options,
    ILogger<InboxRetentionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.EnableRetention || opts.RetentionDays <= 0)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetService<DbContext>();
                if (db is not null
                    && db.Model.FindEntityType(typeof(HRManagement.SharedKernel.Entities.IntegrationMessageReceipt)) is not null)
                {
                    await AdvisoryLeaderElection.TryRunAsLeaderAsync(
                        db,
                        AdvisoryLockKeys.InboxRetention,
                        async ct =>
                        {
                            var cutoff = DateTime.UtcNow.AddDays(-opts.RetentionDays);
                            var deleted = await db.Set<HRManagement.SharedKernel.Entities.IntegrationMessageReceipt>()
                                .Where(r => r.ReceivedAt < cutoff)
                                .ExecuteDeleteAsync(ct);
                            if (deleted > 0)
                                logger.LogInformation(
                                    "Inbox retention deleted {Count} receipts older than {Cutoff:u}", deleted, cutoff);
                        },
                        stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Inbox retention pass failed");
            }

            try
            {
                await Task.Delay(opts.RetentionInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
