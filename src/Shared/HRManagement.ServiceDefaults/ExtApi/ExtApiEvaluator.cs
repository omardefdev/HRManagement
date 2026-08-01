namespace HRManagement.ServiceDefaults.ExtApi;

public static class ExtApiEvaluator
{
    private static readonly string[] RequiredCatalogFields =
        ["integrationId", "contractRef", "owner", "sla"];

    private static readonly string[] RequiredPurposes =
    [
        "payroll-disbursement-export", "civil-registry-proof-query", "agency-onboarding-sync", "audit-evidence-export"
    ];

    private static readonly string[] RequiredAuditFields =
        ["exchangeId", "integrationId", "actor", "tenantId", "outcome", "correlationId", "occurredAt"];

    public static List<string> Evaluate(ExtApiMatrixDocument matrix, bool liveExchangeClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "EXT-005", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be EXT-005, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateCatalog(matrix));
        failures.AddRange(EvaluateVersioning(matrix.Versioning));
        failures.AddRange(EvaluateCompatibility(matrix.Compatibility));
        if (!matrix.SharedDbForbidden)
            failures.Add("sharedDbForbidden must be true");
        failures.AddRange(EvaluatePurposeBoundExport(matrix.PurposeBoundExport));
        failures.AddRange(EvaluateThrottling(matrix.Throttling));
        failures.AddRange(EvaluateNonRepudiation(matrix.NonRepudiation));
        failures.AddRange(EvaluateTenant(matrix.Tenant));
        failures.AddRange(EvaluateErrorModel(matrix.ErrorModel));
        failures.AddRange(EvaluateDeprecation(matrix.Deprecation));
        failures.AddRange(EvaluateDataMinimization(matrix.DataMinimization));
        failures.AddRange(EvaluateSchemaRegistry(matrix.SchemaRegistry));
        failures.AddRange(EvaluateConsumerDriven(matrix.ConsumerDriven));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.ApiExchangePolicyVersion));

        if (liveExchangeClaim)
            failures.Add("LiveExchangeClaim forbidden until EXT-005a-e api-exchange workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ExtApiMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("IAM-004", "Satisfied"),
                     ("TEN-003", "Satisfied"),
                     ("INT-002", "Satisfied"),
                     ("PROD-007", "Satisfied"),
                     ("gov-exchange-bus-live", "Blocked"),
                     ("schema-registry-live", "Blocked"),
                     ("partner-cdc-cert", "Blocked"),
                     ("national-openapi-publish", "Blocked")
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

    public static List<string> EvaluateCatalog(ExtApiMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (!matrix.CatalogRequired)
            failures.Add("catalogRequired must be true");
        foreach (var need in RequiredCatalogFields)
        {
            if (!matrix.RequiredCatalogFields.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"requiredCatalogFields missing {need}");
        }

        return failures;
    }

    public static string? ValidateCatalogEntry(
        IEnumerable<string> presentFields,
        ExtApiMatrixDocument matrix)
    {
        if (!matrix.CatalogRequired)
            return null;
        var required = matrix.RequiredCatalogFields.Count > 0
            ? matrix.RequiredCatalogFields
            : RequiredCatalogFields.ToList();
        var present = presentFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in required)
        {
            if (!present.Contains(need))
                return $"catalog missing field {need}";
        }

        return null;
    }

    public static List<string> EvaluateVersioning(ExtApiVersioning? versioning)
    {
        var failures = new List<string>();
        if (versioning is null)
        {
            failures.Add("versioning missing");
            return failures;
        }

        if (!versioning.SemverRequired)
            failures.Add("versioning.semverRequired must be true");
        if (!versioning.BreakingRequiresMajorBump)
            failures.Add("breakingRequiresMajorBump must be true");
        return failures;
    }

    public static string? ValidateBreakingChange(
        bool isBreaking,
        bool majorBumped,
        ExtApiVersioning? versioning)
    {
        if (isBreaking && !majorBumped && (versioning?.BreakingRequiresMajorBump ?? true))
            return "breaking change without major bump forbidden";
        return null;
    }

    public static List<string> EvaluateCompatibility(ExtApiCompatibility? compat)
    {
        var failures = new List<string>();
        if (compat is null)
        {
            failures.Add("compatibility missing");
            return failures;
        }

        if (!compat.MeasureBackwardCompat)
            failures.Add("measureBackwardCompat must be true");
        if (!compat.SupportNMinus1)
            failures.Add("supportNMinus1 must be true");
        return failures;
    }

    public static string? ValidateSharedDb(bool usesSharedDb, bool sharedDbForbidden)
    {
        if (usesSharedDb && sharedDbForbidden)
            return "shared DB integrations forbidden";
        return null;
    }

    public static List<string> EvaluatePurposeBoundExport(ExtApiPurposeBoundExport? purpose)
    {
        var failures = new List<string>();
        if (purpose is null)
        {
            failures.Add("purposeBoundExport missing");
            return failures;
        }

        if (!purpose.ConsentRequired)
            failures.Add("purposeBoundExport.consentRequired must be true");
        if (purpose.PurposeAllowlist.Count < 4)
            failures.Add("purposeAllowlist too short");
        foreach (var need in RequiredPurposes)
        {
            if (!purpose.PurposeAllowlist.Any(p => string.Equals(p, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"purposeAllowlist missing {need}");
        }

        return failures;
    }

    public static string? ValidatePurposeExport(
        string purposeCode,
        bool consentPresent,
        ExtApiPurposeBoundExport? purpose)
    {
        if (purpose is null)
            return null;
        if (purpose.ConsentRequired && !consentPresent)
            return "consent required for purpose-bound export";
        if (!purpose.PurposeAllowlist.Any(p => string.Equals(p, purposeCode, StringComparison.OrdinalIgnoreCase)))
            return $"purpose '{purposeCode}' not in allowlist";
        return null;
    }

    public static List<string> EvaluateThrottling(ExtApiThrottling? throttling)
    {
        var failures = new List<string>();
        if (throttling is null)
        {
            failures.Add("throttling missing");
            return failures;
        }

        if (throttling.DefaultRpm <= 0)
            failures.Add("throttling.defaultRpm must be > 0");
        if (throttling.Burst <= 0)
            failures.Add("throttling.burst must be > 0");
        if (throttling.OnExceed != 429)
            failures.Add("throttling.onExceed must be 429");
        return failures;
    }

    public static string? ValidateThrottleExceed(bool exceeded, bool returned429, ExtApiThrottling? throttling)
    {
        if (exceeded && !returned429 && throttling?.OnExceed == 429)
            return "throttle exceed must return 429";
        return null;
    }

    public static List<string> EvaluateNonRepudiation(ExtApiNonRepudiation? nonRep)
    {
        var failures = new List<string>();
        if (nonRep is null)
        {
            failures.Add("nonRepudiation missing");
            return failures;
        }

        if (!nonRep.CorrelationIdRequired)
            failures.Add("correlationIdRequired must be true");
        if (!nonRep.AuditRequired)
            failures.Add("nonRepudiation.auditRequired must be true");
        return failures;
    }

    public static List<string> EvaluateTenant(ExtApiTenant? tenant)
    {
        var failures = new List<string>();
        if (tenant is null)
        {
            failures.Add("tenant missing");
            return failures;
        }

        if (!tenant.TenantHeaderRequired)
            failures.Add("tenantHeaderRequired must be true");
        if (!tenant.CrossTenantDeny)
            failures.Add("crossTenantDeny must be true");
        return failures;
    }

    public static List<string> EvaluateErrorModel(ExtApiErrorModel? errorModel)
    {
        if (errorModel is null)
            return ["errorModel missing"];
        if (!errorModel.ProblemDetailsRequired)
            return ["problemDetailsRequired must be true"];
        return [];
    }

    public static List<string> EvaluateDeprecation(ExtApiDeprecation? deprecation)
    {
        var failures = new List<string>();
        if (deprecation is null)
        {
            failures.Add("deprecation missing");
            return failures;
        }

        if (deprecation.NoticeDaysMin < 90)
            failures.Add("noticeDaysMin must be >= 90");
        if (!deprecation.SunsetRequiresMajor)
            failures.Add("sunsetRequiresMajor must be true");
        return failures;
    }

    public static string? ValidateDeprecationWindow(int noticeDays, ExtApiDeprecation? deprecation)
    {
        if (deprecation is null)
            return null;
        if (noticeDays < deprecation.NoticeDaysMin)
            return $"deprecation noticeDays {noticeDays} below minimum {deprecation.NoticeDaysMin}";
        return null;
    }

    public static List<string> EvaluateDataMinimization(ExtApiDataMinimization? min)
    {
        if (min is null)
            return ["dataMinimization missing"];
        if (!min.ForbidExcessFields)
            return ["forbidExcessFields must be true"];
        return [];
    }

    public static List<string> EvaluateSchemaRegistry(ExtApiSchemaRegistry? registry)
    {
        var failures = new List<string>();
        if (registry is null)
        {
            failures.Add("schemaRegistry missing");
            return failures;
        }

        if (!registry.LabMockAllowed)
            failures.Add("schemaRegistry.labMockAllowed must be true");
        if (!registry.LiveForbidden)
            failures.Add("schemaRegistry.liveForbidden must be true");
        return failures;
    }

    public static string? ValidateSchemaCompat(bool compatible)
    {
        if (!compatible)
            return "schema incompatibility treated as success forbidden";
        return null;
    }

    public static List<string> EvaluateConsumerDriven(ExtApiConsumerDriven? consumer)
    {
        if (consumer is null)
            return ["consumerDriven missing"];
        if (!consumer.CertifiedConsumersProtected)
            return ["certifiedConsumersProtected must be true"];
        return [];
    }

    public static string? ValidateCertifiedConsumerBreak(
        bool breaksCertified,
        ExtApiConsumerDriven? consumer)
    {
        if (breaksCertified && (consumer?.CertifiedConsumersProtected ?? true))
            return "breaking certified consumer forbidden";
        return null;
    }

    public static List<string> EvaluateAudit(ExtApiAudit? audit)
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
        ExtApiAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ExtApiMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveBusForbidden)
            failures.Add("liveBusForbidden must be true");
        if (!sandbox.MockCatalog)
            failures.Add("mockSandbox.mockCatalog must be true");
        if (!sandbox.MockRegistry)
            failures.Add("mockSandbox.mockRegistry must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ExtApiSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ExtApiSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.ApiExchangePolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
