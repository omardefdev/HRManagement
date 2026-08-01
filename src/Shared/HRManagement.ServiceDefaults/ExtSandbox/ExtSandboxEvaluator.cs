namespace HRManagement.ServiceDefaults.ExtSandbox;

public static class ExtSandboxEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["partnerId", "credentialId", "suiteId", "outcome", "actor", "occurredAt"];

    public static List<string> Evaluate(ExtSandboxMatrixDocument matrix, bool livePortalClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "EXT-006", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be EXT-006, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        if (!matrix.SyntheticDataOnly)
            failures.Add("syntheticDataOnly must be true");
        if (!matrix.RealPiiForbidden)
            failures.Add("realPiiForbidden must be true");
        failures.AddRange(EvaluateCredentials(matrix.Credentials));
        failures.AddRange(EvaluateConformance(matrix.Conformance));
        failures.AddRange(EvaluateCertification(matrix.Certification));
        if (!matrix.DocsMatchPublishedContract)
            failures.Add("docsMatchPublishedContract must be true");
        failures.AddRange(EvaluateRateError(matrix.RateErrorSimulation));
        failures.AddRange(EvaluateSupportAnalytics(matrix.SupportAnalytics));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.SandboxCertPolicyVersion));

        if (livePortalClaim)
            failures.Add("LiveSandboxPortalClaim forbidden until EXT-006a-e sandbox portal workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ExtSandboxMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("EXT-005", "Satisfied"),
                     ("REL-001", "Satisfied"),
                     ("live-developer-portal", "Blocked"),
                     ("real-pii-in-sandbox", "Blocked"),
                     ("partner-cert-fleet-live", "Blocked"),
                     ("EXT-007", "Satisfied")
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

    public static List<string> EvaluateCredentials(ExtSandboxCredentials? credentials)
    {
        var failures = new List<string>();
        if (credentials is null)
        {
            failures.Add("credentials missing");
            return failures;
        }

        if (!credentials.EphemeralRequired)
            failures.Add("ephemeralRequired must be true");
        if (!credentials.ManualSecretsForbidden)
            failures.Add("manualSecretsForbidden must be true");
        if (!credentials.NoProdAccess)
            failures.Add("noProdAccess must be true");
        return failures;
    }

    public static string? ValidateNoProdAccess(bool attemptedProd, ExtSandboxCredentials? credentials)
    {
        if (attemptedProd && (credentials?.NoProdAccess ?? true))
            return "production access from sandbox forbidden";
        return null;
    }

    public static string? ValidateNoRealPii(bool containsRealPii, bool realPiiForbidden)
    {
        if (containsRealPii && realPiiForbidden)
            return "real PII in sandbox forbidden";
        return null;
    }

    public static string? ValidateEphemeralCredential(
        bool isEphemeral,
        bool isManualSecret,
        ExtSandboxCredentials? credentials)
    {
        if (isManualSecret && (credentials?.ManualSecretsForbidden ?? true))
            return "manual secrets forbidden";
        if (!isEphemeral && (credentials?.EphemeralRequired ?? true))
            return "ephemeral credential required";
        return null;
    }

    public static List<string> EvaluateConformance(ExtSandboxConformance? conformance)
    {
        var failures = new List<string>();
        if (conformance is null)
        {
            failures.Add("conformance missing");
            return failures;
        }

        if (!conformance.NegativeCasesRequired)
            failures.Add("negativeCasesRequired must be true");
        if (!conformance.IncompatibleClientMustFail)
            failures.Add("incompatibleClientMustFail must be true");
        return failures;
    }

    public static string? ValidateIncompatibleClient(
        bool treatedAsCertified,
        bool suitePassed,
        ExtSandboxConformance? conformance)
    {
        if (treatedAsCertified && !suitePassed && (conformance?.IncompatibleClientMustFail ?? true))
            return "incompatible client treated as certified forbidden";
        return null;
    }

    public static List<string> EvaluateCertification(ExtSandboxCertification? certification)
    {
        var failures = new List<string>();
        if (certification is null)
        {
            failures.Add("certification missing");
            return failures;
        }

        if (!certification.RequiredBeforeProdJoin)
            failures.Add("requiredBeforeProdJoin must be true");
        if (!certification.RevocationSupported)
            failures.Add("revocationSupported must be true");
        return failures;
    }

    public static string? ValidateDocsMatch(bool docsMatchContract, bool required)
    {
        if (!docsMatchContract && required)
            return "docs must match published contract";
        return null;
    }

    public static string? ValidateRevocation(
        bool compromised,
        bool revoked,
        ExtSandboxCertification? certification)
    {
        if (compromised && !revoked && (certification?.RevocationSupported ?? true))
            return "compromised credential must be revoked";
        return null;
    }

    public static List<string> EvaluateRateError(ExtSandboxRateErrorSimulation? sim)
    {
        if (sim is null)
            return ["rateErrorSimulation missing"];
        if (!sim.Required)
            return ["rateErrorSimulation.required must be true"];
        return [];
    }

    public static List<string> EvaluateSupportAnalytics(ExtSandboxSupportAnalytics? support)
    {
        var failures = new List<string>();
        if (support is null)
        {
            failures.Add("supportAnalytics missing");
            return failures;
        }

        if (!support.LabStubAllowed)
            failures.Add("supportAnalytics.labStubAllowed must be true");
        if (!support.LiveForbidden)
            failures.Add("supportAnalytics.liveForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateAudit(ExtSandboxAudit? audit)
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
        ExtSandboxAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ExtSandboxMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LivePortalForbidden)
            failures.Add("livePortalForbidden must be true");
        if (!sandbox.RealDataForbidden)
            failures.Add("realDataForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ExtSandboxSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ExtSandboxSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.SandboxCertPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
