using HRManagement.SharedKernel.Policy;
using Microsoft.Extensions.Configuration;

namespace HRManagement.ServiceDefaults.Policy;

/// <summary>PLAN-42 (D-POL-2): what an Enforce call does when HR.Core cannot give a decision.</summary>
public enum PolicyFailureMode
{
    /// <summary>The default: the action is refused.</summary>
    FailClosed,

    /// <summary>The action goes on with an error in the log. Per domain, and never in Production.</summary>
    AllowWithAlert
}

/// <summary>The one place that turns configuration into a policy mode, for HR.Core and for the remote client alike.</summary>
public interface IPolicyModeResolver
{
    /// <summary>
    /// <c>PolicyEngine:{domain}</c>, then <c>PolicyEngine:DefaultMode</c>, then Off. An unknown value throws. PLAN-49: only a domain
    /// the catalog governs has a mode; an out-of-scope domain (and a name that is no domain) is Off whatever the configuration
    /// says, <c>DefaultMode</c> included.
    /// </summary>
    PolicyEnforcementMode Resolve(string domain);

    /// <summary><c>PolicyEngine:FailureMode:{domain}</c>, then FailClosed. An unknown value throws.</summary>
    PolicyFailureMode ResolveFailureMode(string domain);
}

public sealed class PolicyModeResolver(IConfiguration configuration) : IPolicyModeResolver
{
    public const string SectionName = "PolicyEngine";
    public const string DefaultModeKey = "DefaultMode";
    public const string FailureModeKey = "FailureMode";

    public PolicyEnforcementMode Resolve(string domain)
    {
        // PLAN-49 (G-POL-02): the catalog decides first. No key and no default can switch on a domain nobody calls.
        if (!PolicyDomainCatalog.IsGoverned(domain))
            return PolicyEnforcementMode.Off;

        var key = $"{SectionName}:{domain}";
        var raw = configuration[key];
        if (raw is null)
        {
            key = $"{SectionName}:{DefaultModeKey}";
            raw = configuration[key];
        }

        if (raw is null)
            return PolicyEnforcementMode.Off;
        return TryParseMode(raw, out var mode)
            ? mode
            : throw new InvalidOperationException($"{key} is not one of Off, Shadow, Enforce.");
    }

    public PolicyFailureMode ResolveFailureMode(string domain)
    {
        var key = $"{SectionName}:{FailureModeKey}:{domain}";
        var raw = configuration[key];
        if (raw is null)
            return PolicyFailureMode.FailClosed;
        return TryParseFailureMode(raw, out var mode)
            ? mode
            : throw new InvalidOperationException($"{key} is not one of FailClosed, AllowWithAlert.");
    }

    /// <summary>Names only: <c>Enum.TryParse</c> would also take "1" or "Off,Enforce".</summary>
    public static bool TryParseMode(string? value, out PolicyEnforcementMode mode)
    {
        foreach (var candidate in Enum.GetValues<PolicyEnforcementMode>())
        {
            if (string.Equals(value?.Trim(), candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }

        mode = PolicyEnforcementMode.Off;
        return false;
    }

    public static bool TryParseFailureMode(string? value, out PolicyFailureMode mode)
    {
        foreach (var candidate in Enum.GetValues<PolicyFailureMode>())
        {
            if (string.Equals(value?.Trim(), candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }

        mode = PolicyFailureMode.FailClosed;
        return false;
    }
}
