namespace HRManagement.ServiceDefaults.ProdAgency;

public static class ProdAgencyEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["changeId", "agencyId", "scope", "outcome", "actor", "occurredAt", "evidenceRef"];

    private static readonly string[] RequiredScopes =
        ["structure", "roles", "templates", "calendars", "notifications", "quotas"];

    public static List<string> Evaluate(ProdAgencyMatrixDocument matrix, bool liveSelfAdminClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "PROD-002", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be PROD-002, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateDelegatedAdmin(matrix.DelegatedAdmin));
        if (!matrix.NationalControlsImmutable)
            failures.Add("nationalControlsImmutable must be true");
        if (!matrix.CrossTenantAdminForbidden)
            failures.Add("crossTenantAdminForbidden must be true");
        failures.AddRange(EvaluateRollback(matrix.Rollback));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateFourEyes(matrix.FourEyesSensitive));
        failures.AddRange(EvaluatePreview(matrix.Preview));
        failures.AddRange(EvaluateSeparation(matrix.SeparationNationalAgency));
        failures.AddRange(EvaluateSupport(matrix.SupportEscalation));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.AgencySelfAdminPolicyVersion));

        if (liveSelfAdminClaim)
            failures.Add("LiveAgencySelfAdminClaim forbidden until PROD-002a-e agency selfadmin workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ProdAgencyMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("IAM-003", "Satisfied"),
                     ("TEN-003", "Satisfied"),
                     ("PROD-001", "Satisfied"),
                     ("live-agency-selfadmin", "Blocked"),
                     ("live-delegated-admin-ui", "Blocked"),
                     ("cross-tenant-admin", "Blocked"),
                     ("ONB-001", "Satisfied"),
                     ("PROD-003", "Satisfied")
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

    public static List<string> EvaluateDelegatedAdmin(ProdAgencyDelegatedAdmin? admin)
    {
        var failures = new List<string>();
        if (admin is null)
        {
            failures.Add("delegatedAdmin missing");
            return failures;
        }

        if (!admin.Required)
            failures.Add("delegatedAdmin.required must be true");
        if (!admin.NoDbaDeveloperRequired)
            failures.Add("noDbaDeveloperRequired must be true");
        foreach (var need in RequiredScopes)
        {
            if (!admin.Scopes.Any(s => string.Equals(s, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing scope {need}");
        }

        return failures;
    }

    public static string? ValidateDelegatedScope(bool scopeAllowed, bool noDbaPath, ProdAgencyDelegatedAdmin? admin)
    {
        if (!scopeAllowed && (admin?.Required ?? true))
            return "delegated scope not allowed";
        if (!noDbaPath && (admin?.NoDbaDeveloperRequired ?? true))
            return "DBA/developer path forbidden for agency self-admin";
        return null;
    }

    public static string? ValidateNationalControlsImmutable(bool attemptedNationalMutate, bool immutable)
    {
        if (attemptedNationalMutate && immutable)
            return "national control mutation forbidden";
        return null;
    }

    public static string? ValidateCrossTenantForbidden(bool targetTenantEqualsActorTenant, bool forbidden)
    {
        if (!targetTenantEqualsActorTenant && forbidden)
            return "cross-tenant admin forbidden";
        return null;
    }

    public static List<string> EvaluateRollback(ProdAgencyRollback? rollback)
    {
        if (rollback is null)
            return ["rollback missing"];
        if (!rollback.Required)
            return ["rollback.required must be true"];
        return [];
    }

    public static string? ValidateRollback(bool hasRollbackRef, ProdAgencyRollback? rollback)
    {
        if (!hasRollbackRef && (rollback?.Required ?? true))
            return "rollback ref required";
        return null;
    }

    public static List<string> EvaluateAudit(ProdAgencyAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, ProdAgencyAudit? audit)
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

    public static List<string> EvaluateFourEyes(ProdAgencyFourEyesSensitive? fourEyes)
    {
        var failures = new List<string>();
        if (fourEyes is null)
        {
            failures.Add("fourEyesSensitive missing");
            return failures;
        }

        if (!fourEyes.Required)
            failures.Add("fourEyesSensitive.required must be true");
        if (!fourEyes.PublisherRoleEqualsReviewerForbidden)
            failures.Add("publisherRoleEqualsReviewerForbidden must be true");
        return failures;
    }

    public static string? ValidateFourEyesSensitive(
        bool isSensitive,
        string? publisherRole,
        string? reviewerRole,
        ProdAgencyFourEyesSensitive? fourEyes)
    {
        if (!isSensitive)
            return null;
        if (string.IsNullOrWhiteSpace(publisherRole) || string.IsNullOrWhiteSpace(reviewerRole))
            return "sensitive change requires four-eyes roles";
        if (string.Equals(publisherRole, reviewerRole, StringComparison.OrdinalIgnoreCase)
            && (fourEyes?.PublisherRoleEqualsReviewerForbidden ?? true))
            return "publisher role equals reviewer forbidden for sensitive change";
        return null;
    }

    public static List<string> EvaluatePreview(ProdAgencyPreview? preview)
    {
        if (preview is null)
            return ["preview missing"];
        if (!preview.Required)
            return ["preview.required must be true"];
        return [];
    }

    public static string? ValidatePreview(bool previewUsed, ProdAgencyPreview? preview)
    {
        if (!previewUsed && (preview?.Required ?? true))
            return "preview required";
        return null;
    }

    public static List<string> EvaluateSeparation(ProdAgencySeparation? separation)
    {
        if (separation is null)
            return ["separationNationalAgency missing"];
        if (!separation.Required)
            return ["separationNationalAgency.required must be true"];
        return [];
    }

    public static List<string> EvaluateSupport(ProdAgencySupportEscalation? support)
    {
        if (support is null)
            return ["supportEscalation missing"];
        if (!support.Required)
            return ["supportEscalation.required must be true"];
        return [];
    }

    public static List<string> EvaluateMockSandbox(ProdAgencyMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveSelfAdminForbidden)
            failures.Add("liveSelfAdminForbidden must be true");
        if (!sandbox.LiveDelegatedUiForbidden)
            failures.Add("liveDelegatedUiForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ProdAgencySigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ProdAgencySigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.AgencySelfAdminPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
