using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Integration;

/// <summary>
/// PLAN-37 (§4, D-INT-5): the base URL of a service another service calls. Missing outside <c>Development</c> is a
/// start-up error that names the key; in <c>Development</c> outside a container an optional local default is used; in
/// any container a <c>localhost</c> URL is refused (Docker service DNS is required). No S2S client is registered
/// without a URL.
/// </summary>
public static class ServiceEndpoint
{
    private static readonly ConcurrentDictionary<string, string> PendingWarnings = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves <paramref name="key"/>, then each deprecated key (with a start-up warning; a different value than the
    /// approved key is an error). Returns the URL without a trailing slash.
    /// </summary>
    public static string Require(
        IConfiguration configuration,
        string key,
        string? developmentDefault = null,
        IReadOnlyList<string>? deprecatedKeys = null,
        IHostEnvironment? environment = null)
    {
        var value = Read(configuration, key);
        string? usedKey = value is null ? null : key;
        foreach (var legacy in deprecatedKeys ?? [])
        {
            var legacyValue = Read(configuration, legacy);
            if (legacyValue is null)
                continue;
            if (value is not null && !string.Equals(value.TrimEnd('/'), legacyValue.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{key} ({value}) and the deprecated {legacy} ({legacyValue}) disagree. Remove {legacy}; {key} is the approved key.");
            }

            PendingWarnings.TryAdd(legacy, $"Configuration key {legacy} is deprecated; use {key} (PLAN-37, D-INT-3).");
            if (value is null)
            {
                value = legacyValue;
                usedKey = legacy;
            }
        }

        var inContainer = HrGatewayConfigurationExtensions.IsRunningInContainer();
        if (value is null)
        {
            if (developmentDefault is not null && IsDevelopment(configuration, environment) && !inContainer)
                return developmentDefault.TrimEnd('/');

            throw new InvalidOperationException(
                $"{key} is required (the base URL of the called service, e.g. {Example(developmentDefault, key)}); " +
                "it is set by the deployment from deploy/wiring/service-wiring.json.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"{usedKey} must be an absolute http(s) URL (was '{value}').");

        if (inContainer && IsLocalhost(uri))
        {
            throw new InvalidOperationException(
                $"{usedKey} must use Docker service DNS (e.g. {Example(null, key)}) when running in a container. Current value: {value}");
        }

        return value.TrimEnd('/');
    }

    /// <summary>The deprecation warnings collected while the services were registered; logged once at start-up.</summary>
    public static IReadOnlyCollection<string> DeprecationWarnings => PendingWarnings.Values.ToArray();

    /// <summary>Registers the one-time start-up log of <see cref="DeprecationWarnings"/>.</summary>
    public static IServiceCollection AddServiceEndpointWarnings(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ServiceEndpointWarningLogger>());
        return services;
    }

    internal static bool TakeWarning(string legacyKey, out string? message) => PendingWarnings.TryRemove(legacyKey, out message);

    internal static IReadOnlyCollection<string> PendingKeys => PendingWarnings.Keys.ToArray();

    private static string? Read(IConfiguration configuration, string key)
    {
        var raw = configuration[key]?.Trim();
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    private static bool IsDevelopment(IConfiguration configuration, IHostEnvironment? environment)
    {
        if (environment is not null)
            return environment.IsDevelopment();
        var name = configuration[HostDefaults.EnvironmentKey]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environments.Production;
        return string.Equals(name, Environments.Development, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocalhost(Uri uri) =>
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || uri.Host == "127.0.0.1"
        || uri.Host is "::1" or "[::1]";

    private static string Example(string? developmentDefault, string key) => key switch
    {
        HrCoreEndpoint.Key => "http://hr-core-web:8080",
        "Integration:PayrollBaseUrl" => "http://payroll-web:8080",
        "Integration:FileStorageBaseUrl" => "http://file-storage-web:8080",
        "Integration:AuthDirectoryBaseUrl" or "Services:AuthDirectory:BaseUrl" => "http://auth-directory-web:8080",
        HrGatewayConfigurationExtensions.InternalBaseUrlKey => "http://hr-gateway:8080",
        _ => developmentDefault ?? "http://<service>:8080"
    };
}

/// <summary>PLAN-37 (§4, D-INT-3): the HR.Core base URL, one approved key with the deprecated ones as fallbacks.</summary>
public static class HrCoreEndpoint
{
    public const string Key = "Integration:HrCoreBaseUrl";
    public const string DevelopmentDefault = "http://localhost:5010";

    /// <summary>Deprecated keys (D-INT-3): accepted with a start-up warning; removed in wave 2.</summary>
    public static readonly IReadOnlyList<string> DeprecatedKeys = ["Services:HRCore:BaseUrl", "HrCore:BaseUrl"];

    public static string RequireBaseUrl(IConfiguration configuration, IHostEnvironment? environment = null) =>
        ServiceEndpoint.Require(configuration, Key, DevelopmentDefault, DeprecatedKeys, environment);

    /// <summary>The HR.Core base address with a trailing slash, for <see cref="HttpClient.BaseAddress"/>.</summary>
    public static Uri RequireBaseUri(IConfiguration configuration, IHostEnvironment? environment = null) =>
        new(RequireBaseUrl(configuration, environment) + "/");
}

internal sealed class ServiceEndpointWarningLogger(ILogger<ServiceEndpointWarningLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var key in ServiceEndpoint.PendingKeys)
        {
            if (ServiceEndpoint.TakeWarning(key, out var message))
                logger.LogWarning("{DeprecatedConfiguration}", message);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
