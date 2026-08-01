namespace HRManagement.ServiceDefaults.ProdExtensibility;

public static class ProdExtensibilityEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["extensionId", "contractRef", "tenantId", "version", "outcome", "actor", "occurredAt", "evidenceRef"];

    private static readonly string[] RequiredContractTypes = ["apis", "events", "webhooks"];

    public static List<string> Evaluate(ProdExtensibilityMatrixDocument matrix, bool liveExtensibilityClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "PROD-007", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be PROD-007, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateSupportedContracts(matrix.SupportedContracts));
        failures.AddRange(EvaluateSemanticVersioning(matrix.SemanticVersioning));
        failures.AddRange(EvaluateCompatibility(matrix.CompatibilityWindows));
        failures.AddRange(EvaluatePluginIsolation(matrix.PluginIsolation));
        failures.AddRange(EvaluateQuotas(matrix.Quotas));
        failures.AddRange(EvaluateCertification(matrix.Certification));
        failures.AddRange(EvaluateDeprecation(matrix.Deprecation));
        failures.AddRange(EvaluateSdkPolicy(matrix.SdkPolicy));

        if (!matrix.NoDirectDb)
            failures.Add("noDirectDb must be true");
        if (!matrix.NoCustomFork)
            failures.Add("noCustomFork must be true");

        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.ExtensibilityPolicyVersion));

        if (liveExtensibilityClaim)
            failures.Add("LiveExtensibilityClaim forbidden until PROD-007a-e live extensibility workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ProdExtensibilityMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("INT-002", "Satisfied"),
                     ("SUP-001", "Satisfied"),
                     ("STR-004", "Satisfied"),
                     ("live-extension-runtime", "Blocked"),
                     ("live-partner-sdk", "Blocked"),
                     ("live-webhook-delivery", "Blocked"),
                     ("EXT-006", "NotStarted"),
                     ("IOP-001", "NotStarted"),
                     ("REL-004", "NotStarted")
                 })
        {
            var b = matrix.Blockers.FirstOrDefault(x =>
                string.Equals(x.Id, need.Id, StringComparison.OrdinalIgnoreCase));
            if (b is null)
                failures.Add($"blocker {need.Id} missing");
            else if (!string.Equals(b.Status, need.Status, StringComparison.OrdinalIgnoreCase))
                failures.Add($"blocker {need.Id} must be {need.Status} (got {b.Status})");
        }

        return failures;
    }

    public static List<string> EvaluateSupportedContracts(ProdExtensibilitySupportedContracts? contracts)
    {
        var failures = new List<string>();
        if (contracts is null)
        {
            failures.Add("supportedContracts missing");
            return failures;
        }

        if (!contracts.Required)
            failures.Add("supportedContracts.required must be true");
        foreach (var need in RequiredContractTypes)
        {
            if (!contracts.ContractTypes.Any(t => string.Equals(t, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing contractType {need}");
        }

        return failures;
    }

    public static List<string> EvaluateSemanticVersioning(ProdExtensibilitySemanticVersioning? versioning)
    {
        var failures = new List<string>();
        if (versioning is null)
        {
            failures.Add("semanticVersioning missing");
            return failures;
        }

        if (!versioning.Required)
            failures.Add("semanticVersioning.required must be true");
        if (!versioning.SemverRequired)
            failures.Add("semverRequired must be true");
        if (!versioning.BreakingRequiresMajorBump)
            failures.Add("breakingRequiresMajorBump must be true");
        return failures;
    }

    public static List<string> EvaluateCompatibility(ProdExtensibilityCompatibilityWindows? windows)
    {
        var failures = new List<string>();
        if (windows is null)
        {
            failures.Add("compatibilityWindows missing");
            return failures;
        }

        if (!windows.Required)
            failures.Add("compatibilityWindows.required must be true");
        if (!windows.SupportNMinus2)
            failures.Add("supportNMinus2 must be true");
        if (!windows.MeasureBackwardCompat)
            failures.Add("measureBackwardCompat must be true");
        return failures;
    }

    public static string? ValidateN2Compatibility(bool withinWindow, ProdExtensibilityCompatibilityWindows? windows)
    {
        if (!withinWindow && (windows?.SupportNMinus2 ?? true))
            return "N-2 compatibility window required";
        return null;
    }

    public static List<string> EvaluatePluginIsolation(ProdExtensibilityPluginIsolation? isolation)
    {
        var failures = new List<string>();
        if (isolation is null)
        {
            failures.Add("pluginIsolation missing");
            return failures;
        }

        if (!isolation.Required)
            failures.Add("pluginIsolation.required must be true");
        if (!isolation.CrossTenantForbidden)
            failures.Add("crossTenantForbidden must be true");
        if (!isolation.CoreDisruptionForbidden)
            failures.Add("coreDisruptionForbidden must be true");
        return failures;
    }

    public static string? ValidatePluginIsolation(
        bool withinTenant,
        bool coreHealthy,
        ProdExtensibilityPluginIsolation? isolation)
    {
        if (!withinTenant && (isolation?.CrossTenantForbidden ?? true))
            return "cross-tenant extension forbidden";
        if (!coreHealthy && (isolation?.CoreDisruptionForbidden ?? true))
            return "extension must not disrupt core";
        return null;
    }

    public static List<string> EvaluateQuotas(ProdExtensibilityQuotas? quotas)
    {
        var failures = new List<string>();
        if (quotas is null)
        {
            failures.Add("quotas missing");
            return failures;
        }

        if (!quotas.Required)
            failures.Add("quotas.required must be true");
        if (!quotas.EnforceRpm)
            failures.Add("enforceRpm must be true");
        return failures;
    }

    public static string? ValidateQuota(bool withinLimits, ProdExtensibilityQuotas? quotas)
    {
        if (!withinLimits && (quotas?.EnforceRpm ?? true))
            return "extension quota exceeded";
        return null;
    }

    public static List<string> EvaluateCertification(ProdExtensibilityCertification? certification)
    {
        var failures = new List<string>();
        if (certification is null)
        {
            failures.Add("certification missing");
            return failures;
        }

        if (!certification.Required)
            failures.Add("certification.required must be true");
        if (!certification.ConsumerInventoryRequired)
            failures.Add("consumerInventoryRequired must be true");
        if (!certification.LabStubAllowed)
            failures.Add("certification.labStubAllowed must be true");
        if (!certification.LiveForbidden)
            failures.Add("certification.liveForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateDeprecation(ProdExtensibilityDeprecation? deprecation)
    {
        var failures = new List<string>();
        if (deprecation is null)
        {
            failures.Add("deprecation missing");
            return failures;
        }

        if (!deprecation.Required)
            failures.Add("deprecation.required must be true");
        if (deprecation.NoticeDaysMin < 90)
            failures.Add("noticeDaysMin must be >= 90");
        if (!deprecation.MeasuredAnnouncementRequired)
            failures.Add("measuredAnnouncementRequired must be true");
        return failures;
    }

    public static string? ValidateDeprecation(
        bool breakingChange,
        bool noticeSatisfied,
        ProdExtensibilityDeprecation? deprecation)
    {
        if (breakingChange && !noticeSatisfied && (deprecation?.MeasuredAnnouncementRequired ?? true))
            return "breaking change requires measured deprecation notice";
        return null;
    }

    public static List<string> EvaluateSdkPolicy(ProdExtensibilitySdkPolicy? sdk)
    {
        var failures = new List<string>();
        if (sdk is null)
        {
            failures.Add("sdkPolicy missing");
            return failures;
        }

        if (!sdk.Required)
            failures.Add("sdkPolicy.required must be true");
        if (!sdk.LabStubAllowed)
            failures.Add("sdkPolicy.labStubAllowed must be true");
        if (!sdk.LiveSdkForbidden)
            failures.Add("liveSdkForbidden must be true");
        return failures;
    }

    public static string? ValidateNoDirectDb(bool directDbAttempted, bool noDirectDb)
    {
        if (directDbAttempted && noDirectDb)
            return "direct DB access forbidden";
        return null;
    }

    public static string? ValidateNoCustomFork(bool customForkAttempted, bool noCustomFork)
    {
        if (customForkAttempted && noCustomFork)
            return "custom fork forbidden";
        return null;
    }

    public static string? ValidateUpgradePath(bool rehearsalPresent)
    {
        if (!rehearsalPresent)
            return "upgrade path rehearsal required";
        return null;
    }

    public static List<string> EvaluateAudit(ProdExtensibilityAudit? audit)
    {
        var failures = new List<string>();
        if (audit is null)
        {
            failures.Add("audit missing");
            return failures;
        }

        if (!audit.Required)
            failures.Add("audit.required must be true");
        foreach (var need in RequiredAuditFields)
        {
            if (!audit.RequiredFields.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"audit missing {need}");
        }

        return failures;
    }

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, ProdExtensibilityAudit? audit)
    {
        var required = audit?.RequiredFields ?? RequiredAuditFields.ToList();
        var present = presentFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in required)
        {
            if (!present.Contains(need))
                return $"audit missing field {need}";
        }

        return null;
    }

    public static List<string> EvaluateMockSandbox(ProdExtensibilityMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveExtensionRuntimeForbidden)
            failures.Add("liveExtensionRuntimeForbidden must be true");
        if (!sandbox.LivePartnerSdkForbidden)
            failures.Add("livePartnerSdkForbidden must be true");
        if (!sandbox.LiveWebhookDeliveryForbidden)
            failures.Add("liveWebhookDeliveryForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ProdExtensibilitySigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ProdExtensibilitySigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.ExtensibilityPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
