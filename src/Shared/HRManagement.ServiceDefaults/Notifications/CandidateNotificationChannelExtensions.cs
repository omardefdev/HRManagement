using HRManagement.SharedKernel.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Notifications;

/// <summary>
/// COM-030: the channel senders selected by <c>Notifications:Candidate:Provider</c>. With <c>None</c> it holds no sender:
/// <see cref="For"/> returns null and the caller keeps its messages pending (never marks them sent).
/// </summary>
public sealed class NotificationChannelSenderRegistry(
    IEnumerable<INotificationChannelSender> senders,
    IOptions<CandidateNotificationOptions> options)
{
    private readonly IReadOnlyList<INotificationChannelSender> senders = senders.ToList();

    public CandidateNotificationProvider Provider => options.Value.ProviderKind;

    public int MaxAttempts => Math.Max(1, options.Value.MaxAttempts);

    public IReadOnlyList<NotificationChannel> Channels => senders.Select(s => s.Channel).Distinct().ToList();

    /// <summary>The sender of <paramref name="channel"/>, or null when the provider has none (Provider=None).</summary>
    public INotificationChannelSender? For(NotificationChannel channel) =>
        senders.FirstOrDefault(s => s.Channel == channel);
}

/// <summary>
/// COM-030: refuses to start a Production host with <c>Provider=FileDrop</c> (the files hold real addresses), and warns
/// when the chosen provider is missing its settings or is <c>None</c> (messages will stay pending).
/// </summary>
public sealed class CandidateNotificationStartupValidator(
    IOptions<CandidateNotificationOptions> options,
    IHostEnvironment environment,
    ILogger<CandidateNotificationStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var failure = Evaluate(opts, environment);
        if (failure is not null)
            throw new InvalidOperationException("COM-030 candidate notifications: " + failure);

        switch (opts.ProviderKind)
        {
            case CandidateNotificationProvider.None:
                logger.LogWarning(
                    "COM-030 candidate notifications Provider=None: email/SMS messages stay pending until they expire.");
                break;
            case CandidateNotificationProvider.SmtpSms:
                if (string.IsNullOrWhiteSpace(opts.Smtp.Host) || string.IsNullOrWhiteSpace(opts.Smtp.From))
                    logger.LogWarning("COM-030 candidate notifications: SMTP host/sender not configured.");
                if (string.IsNullOrWhiteSpace(opts.Sms.Endpoint))
                    logger.LogWarning("COM-030 candidate notifications: SMS endpoint not configured.");
                break;
        }

        logger.LogInformation(
            "COM-030 candidate notifications provider={Provider} env={Env}", opts.ProviderKind, environment.EnvironmentName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Startup failure, or null when the configuration may run in this environment.</summary>
    public static string? Evaluate(CandidateNotificationOptions opts, IHostEnvironment environment)
    {
        if (opts.ProviderKind == CandidateNotificationProvider.FileDrop && environment.IsProduction())
            return "Provider=FileDrop is a lab adapter and is refused in Production.";
        if (opts.ProviderKind == CandidateNotificationProvider.FileDrop && string.IsNullOrWhiteSpace(opts.FileDrop.Path))
            return "Provider=FileDrop requires Notifications:Candidate:FileDrop:Path.";
        return null;
    }
}

public static class CandidateNotificationChannelExtensions
{
    /// <summary>
    /// COM-030: binds <c>Notifications:Candidate</c> and registers the senders of the configured provider, the
    /// <see cref="NotificationChannelSenderRegistry"/> and the startup validator. <c>None</c> (default) registers no sender;
    /// <c>FileDrop</c> registers a file sender for Email and Sms; <c>Smtp+Sms</c> registers
    /// <see cref="SmtpEmailNotificationSender"/> and <see cref="HttpSmsNotificationSender"/> (named HTTP client with the
    /// outbound timeout + circuit breaker).
    /// </summary>
    public static IServiceCollection AddCandidateNotificationChannels(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(CandidateNotificationOptions.SectionName);
        services.Configure<CandidateNotificationOptions>(section);
        var configured = section.Get<CandidateNotificationOptions>() ?? new CandidateNotificationOptions();

        switch (configured.ProviderKind)
        {
            case CandidateNotificationProvider.FileDrop:
                foreach (var channel in new[] { NotificationChannel.Email, NotificationChannel.Sms })
                    services.AddSingleton<INotificationChannelSender>(sp => new FileDropNotificationSender(
                        channel,
                        sp.GetRequiredService<IOptions<CandidateNotificationOptions>>(),
                        sp.GetRequiredService<IHostEnvironment>()));
                break;
            case CandidateNotificationProvider.SmtpSms:
                var timeout = Math.Clamp(configured.Sms.TimeoutSeconds, 5, 120);
                services.AddHttpClient(HttpSmsNotificationSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(timeout))
                    .AddHrOutboundResilience(timeoutSeconds: timeout);
                services.AddSingleton<INotificationChannelSender, SmtpEmailNotificationSender>();
                services.AddSingleton<INotificationChannelSender, HttpSmsNotificationSender>();
                break;
        }

        services.AddSingleton<NotificationChannelSenderRegistry>();
        services.AddHostedService<CandidateNotificationStartupValidator>();
        return services;
    }
}
