namespace HRManagement.ServiceDefaults.ExtCivil;

public static class ExtCivilEvaluator
{
    private static readonly string[] RequiredPurposes =
    [
        "hire-identity-proof", "payroll-beneficiary-verify", "migration-nid-exception", "dsar-identity-confirm"
    ];

    private static readonly string[] RequiredRequestFields =
        ["nationalId", "purposeCode", "consentRef", "requesterTenantId"];

    private static readonly string[] RequiredResponseFields =
        ["matchStatus", "opaqueSubject", "assuranceHint", "queriedAt"];

    private static readonly string[] RequiredOutcomes = ["Match", "Mismatch", "Unavailable", "Error"];

    private static readonly string[] RequiredAuditFields =
        ["queryId", "purposeCode", "actor", "tenantId", "outcome", "queriedAt"];

    public static List<string> Evaluate(ExtCivilMatrixDocument matrix, bool liveMoiClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "EXT-002", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be EXT-002, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluatePurposeConsent(matrix));
        failures.AddRange(EvaluateMinimalQuery(matrix.MinimalQuery));
        failures.AddRange(EvaluateMatchOutcomes(matrix.MatchOutcomes));
        failures.AddRange(EvaluateMismatch(matrix.MismatchHumanWorkflow));
        failures.AddRange(EvaluateCaching(matrix.Caching));
        failures.AddRange(EvaluateCorrections(matrix.Corrections));
        failures.AddRange(EvaluateUnavailable(matrix.UnavailableMode));
        failures.AddRange(EvaluateRateLimits(matrix.RateLimits));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.CivilRegistryPolicyVersion));

        if (liveMoiClaim)
            failures.Add("LiveMoiClaim forbidden until EXT-002a-d civil registry workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ExtCivilMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("EXT-001", "Satisfied"),
                     ("GOV-001", "Satisfied"),
                     ("MIG-004", "Satisfied"),
                     ("moi-civil-registry-live", "Blocked"),
                     ("live-sandbox-cert", "Blocked"),
                     ("L10N-002", "Done"),
                     ("national-idp-live", "Blocked")
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

    public static List<string> EvaluatePurposeConsent(ExtCivilMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (!matrix.ConsentRequired)
            failures.Add("consentRequired must be true");
        if (matrix.PurposeAllowlist.Count < 3)
            failures.Add("purposeAllowlist too short");
        foreach (var need in RequiredPurposes)
        {
            if (!matrix.PurposeAllowlist.Any(p => string.Equals(p, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"purposeAllowlist missing {need}");
        }

        return failures;
    }

    public static string? ValidatePurpose(string purposeCode, IReadOnlyList<string> allowlist)
    {
        if (!allowlist.Any(p => string.Equals(p, purposeCode, StringComparison.OrdinalIgnoreCase)))
            return $"purpose '{purposeCode}' not in allowlist";
        return null;
    }

    public static string? ValidateConsent(bool consentPresent, bool consentRequired)
    {
        if (consentRequired && !consentPresent)
            return "consent required";
        return null;
    }

    public static List<string> EvaluateMinimalQuery(ExtCivilMinimalQuery? query)
    {
        var failures = new List<string>();
        if (query is null)
        {
            failures.Add("minimalQuery missing");
            return failures;
        }

        foreach (var need in RequiredRequestFields)
        {
            if (!query.AllowedRequestFields.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"request field missing {need}");
        }

        foreach (var need in RequiredResponseFields)
        {
            if (!query.AllowedResponseFields.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"response field missing {need}");
        }

        return failures;
    }

    public static string? ValidateStoredFields(IEnumerable<string> fields, ExtCivilMinimalQuery? query)
    {
        var allowed = query?.AllowedResponseFields ?? RequiredResponseFields.ToList();
        foreach (var f in fields)
        {
            if (!allowed.Any(a => string.Equals(a, f, StringComparison.OrdinalIgnoreCase)))
                return $"stored field '{f}' not allowlisted";
        }

        return null;
    }

    public static List<string> EvaluateMatchOutcomes(IReadOnlyList<string> outcomes)
    {
        var failures = new List<string>();
        foreach (var need in RequiredOutcomes)
        {
            if (!outcomes.Any(o => string.Equals(o, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing match outcome {need}");
        }

        return failures;
    }

    public static List<string> EvaluateMismatch(ExtCivilMismatchWorkflow? workflow)
    {
        if (workflow is null)
            return ["mismatchHumanWorkflow missing"];
        if (!workflow.Required)
            return ["mismatchHumanWorkflow.required must be true"];
        return [];
    }

    public static string? ValidateMismatchWorkflow(bool isMismatch, bool humanQueued, ExtCivilMismatchWorkflow? workflow)
    {
        if (isMismatch && (workflow?.Required ?? true) && !humanQueued)
            return "mismatch requires human workflow";
        return null;
    }

    public static List<string> EvaluateCaching(ExtCivilCaching? caching)
    {
        var failures = new List<string>();
        if (caching is null)
        {
            failures.Add("caching missing");
            return failures;
        }

        if (!caching.ForbidStaleAsTruth)
            failures.Add("forbidStaleAsTruth must be true");
        if (caching.TtlSeconds <= 0)
            failures.Add("ttlSeconds must be > 0");
        return failures;
    }

    public static string? ValidateStaleAsTruth(bool stale, bool usedAsTruth, ExtCivilCaching? caching)
    {
        if (stale && usedAsTruth && (caching?.ForbidStaleAsTruth ?? true))
            return "stale cache as truth forbidden";
        return null;
    }

    public static List<string> EvaluateCorrections(ExtCivilCorrections? corrections)
    {
        if (corrections is null)
            return ["corrections missing"];
        if (!corrections.WorkflowRequired)
            return ["corrections.workflowRequired must be true"];
        return [];
    }

    public static List<string> EvaluateUnavailable(ExtCivilUnavailableMode? mode)
    {
        if (mode is null)
            return ["unavailableMode missing"];
        if (!mode.ForbidUnverifiedAsTruth)
            return ["forbidUnverifiedAsTruth must be true"];
        return [];
    }

    public static string? ValidateOutageAsTruth(
        bool registryAvailable,
        bool treatedAsVerified,
        ExtCivilUnavailableMode? mode)
    {
        if (!registryAvailable && treatedAsVerified && (mode?.ForbidUnverifiedAsTruth ?? true))
            return "outage must not become verified truth";
        return null;
    }

    public static List<string> EvaluateRateLimits(ExtCivilRateLimits? limits)
    {
        var failures = new List<string>();
        if (limits is null)
        {
            failures.Add("rateLimits missing");
            return failures;
        }

        if (!limits.BulkHarvestForbidden)
            failures.Add("bulkHarvestForbidden must be true");
        if (limits.MaxQueriesPerMinute <= 0)
            failures.Add("maxQueriesPerMinute must be > 0");
        return failures;
    }

    public static string? ValidateBulkHarvest(bool attempted, ExtCivilRateLimits? limits)
    {
        if (attempted && (limits?.BulkHarvestForbidden ?? true))
            return "bulk harvest forbidden";
        return null;
    }

    public static List<string> EvaluateAudit(ExtCivilAudit? audit)
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

    public static string? ValidateAuditCompleteness(
        IEnumerable<string> presentFields,
        ExtCivilAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ExtCivilMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveMoiForbidden)
            failures.Add("liveMoiForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ExtCivilSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ExtCivilSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.CivilRegistryPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
