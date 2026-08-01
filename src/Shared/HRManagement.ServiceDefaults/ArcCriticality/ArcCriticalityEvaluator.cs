namespace HRManagement.ServiceDefaults.ArcCriticality;

public static class ArcCriticalityEvaluator
{
    private static readonly string[] RequiredAuditFields =
    [
        "componentId", "tier", "dependencyRef", "failureDomainRef", "sloRef", "telemetryRef", "recoveryStepRef",
        "actor", "occurredAt", "evidenceRef", "outcome", "spofExceptionRef"
    ];

    private static readonly string[] RequiredSurfaces =
        ["dns", "pki", "kms", "registry", "ci", "telemetry"];

    private static readonly string[] RequiredTiers = ["0", "1", "2"];

    public static List<string> Evaluate(ArcCriticalityMatrixDocument matrix, bool liveCriticalityClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "ARC-001", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be ARC-001, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateTierClassification(matrix.TierClassification));
        failures.AddRange(EvaluateJourneyMap(matrix.JourneyDependencyMap));
        failures.AddRange(EvaluateSharedPlatform(matrix.SharedPlatformDeps));
        failures.AddRange(EvaluateFailureDomains(matrix.FailureDomains));
        failures.AddRange(EvaluateNumericTargets(matrix.NumericTargetsTelemetry));
        failures.AddRange(EvaluateSpofPolicy(matrix.SpofPolicy));
        failures.AddRange(EvaluateRecoveryOrder(matrix.RecoveryOrder));
        failures.AddRange(EvaluateGracefulDegradation(matrix.GracefulDegradation));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.CriticalityPolicyVersion));

        if (liveCriticalityClaim)
            failures.Add("LiveCriticalityClaim forbidden until ARC-001a live architecture failure walkthrough");

        return failures;
    }

    public static List<string> EvaluateBlockers(ArcCriticalityMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("STR-001", "Satisfied"),
                     ("OBS-001", "Satisfied"),
                     ("OPS-001", "Satisfied"),
                     ("SOV-007", "Satisfied"),
                     ("live-architecture-failure-walkthrough", "Blocked"),
                     ("live-controlled-dependency-shutdown", "Blocked"),
                     ("live-recovery-order-drill", "Blocked"),
                     ("live-full-dependency-discovery", "Blocked"),
                     ("ARC-002", "Satisfied"),
                     ("PHY-001", "Satisfied")
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

    public static List<string> EvaluateTierClassification(ArcCriticalityTiers? tiers)
    {
        var failures = new List<string>();
        if (tiers is null)
        {
            failures.Add("tierClassification missing");
            return failures;
        }

        if (!tiers.Required)
            failures.Add("tierClassification.required must be true");
        if (!tiers.AllServicesClassifiedRequired)
            failures.Add("allServicesClassifiedRequired must be true");
        if (!tiers.AllDependenciesClassifiedRequired)
            failures.Add("allDependenciesClassifiedRequired must be true");
        foreach (var need in RequiredTiers)
        {
            if (!tiers.Tiers.Any(t => string.Equals(t, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"tierClassification missing tier {need}");
        }

        return failures;
    }

    public static string? ValidateClassification(bool allServicesClassified, bool allDependenciesClassified, ArcCriticalityTiers? tiers)
    {
        if (!allServicesClassified && (tiers?.AllServicesClassifiedRequired ?? true))
            return "all services must be classified";
        if (!allDependenciesClassified && (tiers?.AllDependenciesClassifiedRequired ?? true))
            return "all dependencies must be classified";
        return null;
    }

    public static List<string> EvaluateJourneyMap(ArcCriticalityJourneyMap? map)
    {
        var failures = new List<string>();
        if (map is null)
        {
            failures.Add("journeyDependencyMap missing");
            return failures;
        }

        if (!map.Required)
            failures.Add("journeyDependencyMap.required must be true");
        if (!map.CriticalJourneysMappedRequired)
            failures.Add("criticalJourneysMappedRequired must be true");
        return failures;
    }

    public static string? ValidateJourneyMap(bool criticalJourneysMapped, ArcCriticalityJourneyMap? map)
    {
        if (!criticalJourneysMapped && (map?.CriticalJourneysMappedRequired ?? true))
            return "critical journeys must be mapped";
        return null;
    }

    public static List<string> EvaluateSharedPlatform(ArcCriticalitySharedPlatform? platform)
    {
        var failures = new List<string>();
        if (platform is null)
        {
            failures.Add("sharedPlatformDeps missing");
            return failures;
        }

        if (!platform.Required)
            failures.Add("sharedPlatformDeps.required must be true");
        foreach (var need in RequiredSurfaces)
        {
            if (!platform.Surfaces.Any(s => string.Equals(s, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"sharedPlatformDeps missing {need}");
        }

        return failures;
    }

    public static string? ValidateSharedPlatform(IEnumerable<string> missingSurfaces)
    {
        var missing = missingSurfaces.ToList();
        if (missing.Count > 0)
            return "shared platform surfaces missing: " + string.Join(", ", missing);
        return null;
    }

    public static List<string> EvaluateFailureDomains(ArcCriticalityFailureDomains? domains)
    {
        var failures = new List<string>();
        if (domains is null)
        {
            failures.Add("failureDomains missing");
            return failures;
        }

        if (!domains.Required)
            failures.Add("failureDomains.required must be true");
        if (!domains.DomainsDocumentedRequired)
            failures.Add("domainsDocumentedRequired must be true");
        return failures;
    }

    public static string? ValidateFailureDomains(bool domainsDocumented, ArcCriticalityFailureDomains? domains)
    {
        if (!domainsDocumented && (domains?.DomainsDocumentedRequired ?? true))
            return "failure domains must be documented";
        return null;
    }

    public static List<string> EvaluateNumericTargets(ArcCriticalityNumericTargets? targets)
    {
        var failures = new List<string>();
        if (targets is null)
        {
            failures.Add("numericTargetsTelemetry missing");
            return failures;
        }

        if (!targets.Required)
            failures.Add("numericTargetsTelemetry.required must be true");
        if (!targets.EveryNumericTargetHasTelemetryRequired)
            failures.Add("everyNumericTargetHasTelemetryRequired must be true");
        return failures;
    }

    public static string? ValidateTelemetryBinding(bool targetWithoutTelemetry, ArcCriticalityNumericTargets? targets)
    {
        if (targetWithoutTelemetry && (targets?.EveryNumericTargetHasTelemetryRequired ?? true))
            return "numeric target without telemetry forbidden";
        return null;
    }

    public static List<string> EvaluateSpofPolicy(ArcCriticalitySpofPolicy? spof)
    {
        var failures = new List<string>();
        if (spof is null)
        {
            failures.Add("spofPolicy missing");
            return failures;
        }

        if (!spof.Required)
            failures.Add("spofPolicy.required must be true");
        if (!spof.NoUnacceptableSpofInTier01Required)
            failures.Add("noUnacceptableSpofInTier01Required must be true");
        return failures;
    }

    public static string? ValidateSpof(bool unacceptableSpofInTier01, ArcCriticalitySpofPolicy? spof)
    {
        if (unacceptableSpofInTier01 && (spof?.NoUnacceptableSpofInTier01Required ?? true))
            return "unacceptable SPOF in Tier-0/1 forbidden";
        return null;
    }

    public static List<string> EvaluateRecoveryOrder(ArcCriticalityRecoveryOrder? recovery)
    {
        var failures = new List<string>();
        if (recovery is null)
        {
            failures.Add("recoveryOrder missing");
            return failures;
        }

        if (!recovery.Required)
            failures.Add("recoveryOrder.required must be true");
        if (!recovery.OrderedStepsRequired)
            failures.Add("orderedStepsRequired must be true");
        if (!recovery.DrillEvidenceRequired)
            failures.Add("drillEvidenceRequired must be true");
        return failures;
    }

    public static string? ValidateRecoveryOrder(
        bool orderedStepsPresent,
        bool drillEvidencePresent,
        ArcCriticalityRecoveryOrder? recovery)
    {
        if (!orderedStepsPresent && (recovery?.OrderedStepsRequired ?? true))
            return "recovery ordered steps required";
        if (!drillEvidencePresent && (recovery?.DrillEvidenceRequired ?? true))
            return "recovery drill evidence required";
        return null;
    }

    public static List<string> EvaluateGracefulDegradation(ArcCriticalityGracefulDegradation? degradation)
    {
        var failures = new List<string>();
        if (degradation is null)
        {
            failures.Add("gracefulDegradation missing");
            return failures;
        }

        if (!degradation.Required)
            failures.Add("gracefulDegradation.required must be true");
        if (!degradation.Tier01DegradationDocumentedRequired)
            failures.Add("tier01DegradationDocumentedRequired must be true");
        return failures;
    }

    public static string? ValidateGracefulDegradation(bool tier01Documented, ArcCriticalityGracefulDegradation? degradation)
    {
        if (!tier01Documented && (degradation?.Tier01DegradationDocumentedRequired ?? true))
            return "Tier-0/1 graceful degradation must be documented";
        return null;
    }

    public static List<string> EvaluateAudit(ArcCriticalityAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, ArcCriticalityAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ArcCriticalityMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveArchitectureFailureWalkthroughForbidden)
            failures.Add("liveArchitectureFailureWalkthroughForbidden must be true");
        if (!sandbox.LiveControlledDependencyShutdownForbidden)
            failures.Add("liveControlledDependencyShutdownForbidden must be true");
        if (!sandbox.LiveRecoveryOrderDrillForbidden)
            failures.Add("liveRecoveryOrderDrillForbidden must be true");
        if (!sandbox.LiveFullDependencyDiscoveryForbidden)
            failures.Add("liveFullDependencyDiscoveryForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ArcCriticalitySigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ArcCriticalitySigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.CriticalityPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
