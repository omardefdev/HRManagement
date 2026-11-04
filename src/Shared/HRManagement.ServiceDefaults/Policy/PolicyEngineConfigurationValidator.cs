using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Policy;

public sealed class PolicyEngineValidationOptions
{
    /// <summary>True when the host calls HR.Core for decisions, so a domain that is not Off needs its address.</summary>
    public bool UsesRemoteClient { get; set; }
}

/// <summary>
/// PLAN-42: refuses to start on a <c>PolicyEngine</c> section that would not do what it says: a mode that is not Off, Shadow
/// or Enforce; a key that is not in <see cref="PolicyDomains.All"/>; an unknown failure mode; <c>AllowWithAlert</c> in
/// Production (D-POL-2); and, on a remote client outside Development, a domain that is not Off with no HR.Core address.
/// Names the key, never a value.
/// </summary>
public sealed class PolicyEngineConfigurationValidator(
    IConfiguration configuration,
    IHostEnvironment environment,
    IOptions<PolicyEngineValidationOptions> options) : IHrStartupCheck
{
    public string Name => "policy-engine-configuration";

    public Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = Evaluate(configuration, environment, options.Value);
        return failures.Count == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException(
                "إعداد محرك السياسات غير صالح، لن تبدأ الخدمة. PolicyEngine configuration is invalid: "
                + string.Join("; ", failures));
    }

    public static IReadOnlyList<string> Evaluate(
        IConfiguration configuration,
        IHostEnvironment environment,
        PolicyEngineValidationOptions options)
    {
        var failures = new List<string>();
        var anyActive = false;

        foreach (var child in configuration.GetSection(PolicyModeResolver.SectionName).GetChildren())
        {
            if (string.Equals(child.Key, PolicyModeResolver.FailureModeKey, StringComparison.OrdinalIgnoreCase))
            {
                EvaluateFailureModes(child, environment, failures);
                continue;
            }

            var isDefault = string.Equals(child.Key, PolicyModeResolver.DefaultModeKey, StringComparison.OrdinalIgnoreCase);
            if (!isDefault && !IsDomain(child.Key))
                failures.Add($"{child.Path} is not a policy domain");
            if (!PolicyModeResolver.TryParseMode(child.Value, out var mode))
                failures.Add($"{child.Path} is not one of Off, Shadow, Enforce");
            else if (mode != PolicyEnforcementMode.Off)
                anyActive = true;
        }

        if (options.UsesRemoteClient
            && anyActive
            && !environment.IsDevelopment()
            && string.IsNullOrWhiteSpace(configuration[HrCoreEndpoint.Key]))
        {
            failures.Add($"{HrCoreEndpoint.Key} is required: a policy domain is not Off");
        }

        return failures;
    }

    private static void EvaluateFailureModes(IConfigurationSection section, IHostEnvironment environment, List<string> failures)
    {
        foreach (var child in section.GetChildren())
        {
            if (!IsDomain(child.Key))
                failures.Add($"{child.Path} is not a policy domain");
            if (!PolicyModeResolver.TryParseFailureMode(child.Value, out var mode))
                failures.Add($"{child.Path} is not one of FailClosed, AllowWithAlert");
            else if (mode == PolicyFailureMode.AllowWithAlert && environment.IsProduction())
                failures.Add($"{child.Path} is AllowWithAlert in Production (D-POL-2)");
        }
    }

    private static bool IsDomain(string key) => PolicyDomains.All.Contains(key, StringComparer.OrdinalIgnoreCase);
}

public static class PolicyEngineConfigurationExtensions
{
    /// <summary>The resolver and the start-up check. HR.Core calls this; the remote client registration calls it too.</summary>
    public static IServiceCollection AddHrPolicyEngineConfiguration(this IServiceCollection services)
    {
        services.TryAddSingleton<IPolicyModeResolver, PolicyModeResolver>();
        if (services.All(d => d.ServiceType != typeof(PolicyEngineConfigurationValidator)))
            services.AddHrStartupCheck<PolicyEngineConfigurationValidator>();
        return services;
    }
}
