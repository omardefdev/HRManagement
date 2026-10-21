namespace HRManagement.ServiceDefaults.ProdPolicy;

public static class ProdPolicyEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["templateId", "kind", "version", "outcome", "actor", "occurredAt", "evidenceRef"];

    private static readonly string[] RequiredKinds = ["policy", "rule", "workflow", "form"];

    public static List<string> Evaluate(ProdPolicyMatrixDocument matrix, bool liveEngineClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "PROD-001", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be PROD-001, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateVersionedTemplates(matrix.VersionedTemplates));
        failures.AddRange(EvaluateEffectiveDating(matrix.EffectiveDating));
        failures.AddRange(EvaluateFourEyes(matrix.FourEyesPublish));
        failures.AddRange(EvaluateSimulation(matrix.Simulation));
        failures.AddRange(EvaluateRollback(matrix.Rollback));
        failures.AddRange(EvaluateTenantOverride(matrix.TenantOverride));
        if (!matrix.NoArbitraryCode)
            failures.Add("noArbitraryCode must be true");
        if (!matrix.InvalidPublishForbidden)
            failures.Add("invalidPublishForbidden must be true");
        if (!matrix.ChangeWithoutDeployOrFork)
            failures.Add("changeWithoutDeployOrFork must be true");
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.PolicyPlatformPolicyVersion));

        if (liveEngineClaim)
        {
            var engineOk = matrix.Blockers.Any(b =>
                string.Equals(b.Id, "live-policy-engine", StringComparison.OrdinalIgnoreCase)
                && string.Equals(b.Status, "Satisfied", StringComparison.OrdinalIgnoreCase));
            var wfOk = matrix.Blockers.Any(b =>
                string.Equals(b.Id, "live-workflow-runtime", StringComparison.OrdinalIgnoreCase)
                && string.Equals(b.Status, "Satisfied", StringComparison.OrdinalIgnoreCase));
            if (!engineOk || !wfOk)
                failures.Add("LivePolicyEngineClaim requires live-policy-engine and live-workflow-runtime Satisfied");
        }

        return failures;
    }

    public static List<string> EvaluateBlockers(ProdPolicyMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("PAY-002", "Satisfied"),
                     ("GOV-002", "Satisfied"),
                     ("STR-002", "Satisfied"),
                     ("live-policy-engine", "Satisfied"),
                     ("live-workflow-runtime", "Satisfied"),
                     ("live-form-designer", "Blocked"),
                     ("PROD-002", "Satisfied"),
                     ("ONB-001", "Satisfied")
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

    public static List<string> EvaluateVersionedTemplates(ProdPolicyVersionedTemplates? templates)
    {
        var failures = new List<string>();
        if (templates is null)
        {
            failures.Add("versionedTemplates missing");
            return failures;
        }

        if (!templates.Required)
            failures.Add("versionedTemplates.required must be true");
        foreach (var need in RequiredKinds)
        {
            if (!templates.Kinds.Any(k => string.Equals(k, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing kind {need}");
        }

        return failures;
    }

    public static string? ValidateVersionedTemplate(bool hasVersion, string? kind, ProdPolicyVersionedTemplates? templates)
    {
        if (!hasVersion && (templates?.Required ?? true))
            return "versioned template required";
        if (!string.IsNullOrWhiteSpace(kind)
            && templates is not null
            && !templates.Kinds.Any(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase)))
            return $"kind '{kind}' not allowed";
        return null;
    }

    public static List<string> EvaluateEffectiveDating(ProdPolicyEffectiveDating? dating)
    {
        var failures = new List<string>();
        if (dating is null)
        {
            failures.Add("effectiveDating missing");
            return failures;
        }

        if (!dating.Required)
            failures.Add("effectiveDating.required must be true");
        if (!dating.HistoricalVersionPinned)
            failures.Add("historicalVersionPinned must be true");
        return failures;
    }

    public static string? ValidateHistoricalPin(bool resolvedVersionMatchesPin, ProdPolicyEffectiveDating? dating)
    {
        if (!resolvedVersionMatchesPin && (dating?.HistoricalVersionPinned ?? true))
            return "historical version pin mismatch";
        return null;
    }

    public static List<string> EvaluateFourEyes(ProdPolicyFourEyesPublish? fourEyes)
    {
        var failures = new List<string>();
        if (fourEyes is null)
        {
            failures.Add("fourEyesPublish missing");
            return failures;
        }

        if (!fourEyes.Required)
            failures.Add("fourEyesPublish.required must be true");
        if (!fourEyes.PublisherRoleEqualsReviewerForbidden)
            failures.Add("publisherRoleEqualsReviewerForbidden must be true");
        return failures;
    }

    public static string? ValidateFourEyesPublish(
        string? publisherRole,
        string? reviewerRole,
        ProdPolicyFourEyesPublish? fourEyes)
    {
        if (string.IsNullOrWhiteSpace(publisherRole) || string.IsNullOrWhiteSpace(reviewerRole))
            return "four-eyes roles required";
        if (string.Equals(publisherRole, reviewerRole, StringComparison.OrdinalIgnoreCase)
            && (fourEyes?.PublisherRoleEqualsReviewerForbidden ?? true))
            return "publisher role equals reviewer forbidden";
        return null;
    }

    public static List<string> EvaluateSimulation(ProdPolicySimulation? simulation)
    {
        var failures = new List<string>();
        if (simulation is null)
        {
            failures.Add("simulation missing");
            return failures;
        }

        if (!simulation.Required)
            failures.Add("simulation.required must be true");
        if (!simulation.SyntheticOnly)
            failures.Add("syntheticOnly must be true");
        if (!simulation.RealDataForbidden)
            failures.Add("realDataForbidden must be true");
        return failures;
    }

    public static string? ValidateSimulationSynthetic(bool usesRealData, ProdPolicySimulation? simulation)
    {
        if (usesRealData && (simulation?.RealDataForbidden ?? true))
            return "simulation real data forbidden";
        return null;
    }

    public static string? ValidateInvalidPublish(bool isValid, bool published, bool invalidPublishForbidden)
    {
        if (!isValid && published && invalidPublishForbidden)
            return "invalid policy publish forbidden";
        return null;
    }

    public static List<string> EvaluateRollback(ProdPolicyRollback? rollback)
    {
        if (rollback is null)
            return ["rollback missing"];
        if (!rollback.Required)
            return ["rollback.required must be true"];
        return [];
    }

    public static string? ValidateRollback(bool hasRollbackRef, ProdPolicyRollback? rollback)
    {
        if (!hasRollbackRef && (rollback?.Required ?? true))
            return "rollback ref required";
        return null;
    }

    public static List<string> EvaluateTenantOverride(ProdPolicyTenantOverride? overridePolicy)
    {
        var failures = new List<string>();
        if (overridePolicy is null)
        {
            failures.Add("tenantOverride missing");
            return failures;
        }

        if (!overridePolicy.Allowed)
            failures.Add("tenantOverride.allowed must be true");
        if (!overridePolicy.CannotBypassNationalMandatory)
            failures.Add("cannotBypassNationalMandatory must be true");
        return failures;
    }

    public static string? ValidateOverrideCeiling(bool overrideTouchesNationalMandatory, ProdPolicyTenantOverride? policy)
    {
        if (overrideTouchesNationalMandatory && (policy?.CannotBypassNationalMandatory ?? true))
            return "override cannot bypass national mandatory controls";
        return null;
    }

    public static string? ValidateNoArbitraryCode(bool containsExecutableCode, bool noArbitraryCode)
    {
        if (containsExecutableCode && noArbitraryCode)
            return "arbitrary code forbidden";
        return null;
    }

    public static List<string> EvaluateAudit(ProdPolicyAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, ProdPolicyAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ProdPolicyMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveEngineForbidden)
            failures.Add("liveEngineForbidden must be true");
        if (!sandbox.LivePublishForbidden)
            failures.Add("livePublishForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ProdPolicySigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ProdPolicySigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.PolicyPlatformPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
