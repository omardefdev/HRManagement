namespace HRManagement.ServiceDefaults.ExtFederation;

public static class ExtFederationEvaluator
{
    private static readonly string[] RequiredAals = ["AAL1", "AAL2", "AAL3"];
    private static readonly string[] RequiredMetadata =
        ["entityId", "signingCert", "ssoEndpoint", "logoutEndpoint"];
    private static readonly string[] RequiredSensitiveRoles =
        ["payroll-operator", "security-admin", "break-glass-operator"];
    private static readonly string[] RequiredLifecycleStates =
        ["provision", "active", "disable", "revoke"];

    private static readonly Dictionary<string, int> AalRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AAL1"] = 1,
        ["AAL2"] = 2,
        ["AAL3"] = 3
    };

    public static List<string> Evaluate(ExtFederationMatrixDocument matrix, bool liveFederationClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "EXT-001", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be EXT-001, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateAssuranceLevels(matrix));
        failures.AddRange(EvaluatePersonas(matrix));
        failures.AddRange(EvaluateFederationTrust(matrix.FederationTrust));
        failures.AddRange(EvaluateMfa(matrix.MfaPolicy));
        failures.AddRange(EvaluateLifecycle(matrix.Lifecycle));
        failures.AddRange(EvaluateRevocation(matrix.Revocation));
        failures.AddRange(EvaluateBreakGlass(matrix.BreakGlass));
        failures.AddRange(EvaluateSeparation(matrix.CitizenEmployeeSeparation));
        failures.AddRange(EvaluateAttributeAuthority(matrix.AttributeAuthority));
        failures.AddRange(EvaluateIdentifierPolicy(matrix.IdentifierPolicy));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.AssurancePolicyVersion));

        if (liveFederationClaim)
            failures.Add("LiveFederationClaim forbidden until EXT-001a-d federation workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ExtFederationMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("IAM-003", "Satisfied"),
                     ("IAM-004", "Satisfied"),
                     ("STR-002", "Satisfied"),
                     ("IAM-001", "Blocked"),
                     ("national-idp-live", "Blocked"),
                     ("agency-idp-live", "Blocked"),
                     ("phishing-mfa-fleet", "Blocked"),
                     ("live-federation-conformance", "Blocked")
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

    public static List<string> EvaluateAssuranceLevels(ExtFederationMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.AssuranceLevels.Count < 3)
            failures.Add($"need >= 3 assuranceLevels (got {matrix.AssuranceLevels.Count})");
        foreach (var need in RequiredAals)
        {
            if (!matrix.AssuranceLevels.Any(a => string.Equals(a.Id, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing assurance level {need}");
        }

        return failures;
    }

    public static List<string> EvaluatePersonas(ExtFederationMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.Personas.Count < 5)
            failures.Add($"need >= 5 personas (got {matrix.Personas.Count})");

        foreach (var p in matrix.Personas)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.MinAal) || string.IsNullOrWhiteSpace(p.Risk))
                failures.Add($"persona {p.Id} incomplete");
            else if ((string.Equals(p.Risk, "High", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(p.Risk, "Critical", StringComparison.OrdinalIgnoreCase))
                     && string.Equals(p.MinAal, "AAL1", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"persona {p.Id} High/Critical cannot be AAL1");
            }
        }

        foreach (var need in RequiredSensitiveRoles)
        {
            var p = matrix.Personas.FirstOrDefault(x =>
                string.Equals(x.Id, need, StringComparison.OrdinalIgnoreCase));
            if (p is null)
                failures.Add($"missing sensitive persona {need}");
            else if (!string.Equals(p.MinAal, "AAL3", StringComparison.OrdinalIgnoreCase))
                failures.Add($"persona {need} must require AAL3");
        }

        return failures;
    }

    public static string? ValidatePersonaAssurance(
        string personaId,
        string aal,
        IReadOnlyList<ExtPersona> personas)
    {
        var p = personas.FirstOrDefault(x =>
            string.Equals(x.Id, personaId, StringComparison.OrdinalIgnoreCase));
        if (p is null)
            return $"unknown persona {personaId}";
        if (!AalRank.TryGetValue(aal, out var have) || !AalRank.TryGetValue(p.MinAal, out var need))
            return $"invalid AAL for persona {personaId}";
        if (have < need)
            return $"persona {personaId} requires {p.MinAal}";
        return null;
    }

    public static List<string> EvaluateFederationTrust(ExtFederationTrust? trust)
    {
        var failures = new List<string>();
        if (trust is null)
        {
            failures.Add("federationTrust missing");
            return failures;
        }

        if (trust.NationalIdp is null)
            failures.Add("nationalIdp missing");
        else
        {
            if (trust.NationalIdp.Live)
                failures.Add("national IdP must not be live in this binding");
            if (!trust.NationalIdp.LabStub)
                failures.Add("nationalIdp.labStub must be true");
        }

        if (trust.AgencyIdps.Count < 1)
            failures.Add("need >= 1 agency IdP stub");
        foreach (var a in trust.AgencyIdps)
        {
            if (a.Live)
                failures.Add($"agency IdP {a.Id} must not be live in this binding");
            if (!a.LabStub)
                failures.Add($"agency IdP {a.Id} labStub must be true");
        }

        if (trust.MetadataRequirements.Count < 4)
            failures.Add("metadataRequirements incomplete");
        foreach (var need in RequiredMetadata)
        {
            if (!trust.MetadataRequirements.Any(m => string.Equals(m, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing metadata requirement {need}");
        }

        return failures;
    }

    public static string? ValidateLiveIdpClaim(bool nationalLive, bool agencyLive, ExtFederationTrust? trust)
    {
        if (nationalLive && (trust?.NationalIdp?.Live == false || trust?.NationalIdp is null))
            return "national IdP live claim forbidden in this binding";
        if (agencyLive)
            return "agency IdP live claim forbidden in this binding";
        if (nationalLive)
            return "national IdP live claim forbidden in this binding";
        return null;
    }

    public static List<string> EvaluateMfa(ExtMfaPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("mfaPolicy missing");
            return failures;
        }

        if (policy.PhishingResistantRoles.Count < 1)
            failures.Add("phishingResistantRoles empty");
        foreach (var need in RequiredSensitiveRoles)
        {
            if (!policy.PhishingResistantRoles.Any(r => string.Equals(r, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"phishingResistantRoles missing {need}");
        }

        if (!policy.SmsOtpForbiddenForSensitive)
            failures.Add("smsOtpForbiddenForSensitive must be true");
        return failures;
    }

    public static string? ValidateSmsOtpForSensitive(
        string role,
        bool usedSmsOtp,
        ExtMfaPolicy? policy)
    {
        if (!usedSmsOtp) return null;
        if (!(policy?.SmsOtpForbiddenForSensitive ?? true)) return null;
        var sensitive = policy?.PhishingResistantRoles ?? RequiredSensitiveRoles.ToList();
        if (sensitive.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            return "SMS OTP forbidden for sensitive roles";
        return null;
    }

    public static List<string> EvaluateLifecycle(ExtLifecyclePolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("lifecycle missing");
            return failures;
        }

        foreach (var need in RequiredLifecycleStates)
        {
            if (!policy.States.Any(s => string.Equals(s, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"lifecycle missing state {need}");
        }

        if (!policy.RevokeRequiresReason)
            failures.Add("revokeRequiresReason must be true");
        return failures;
    }

    public static List<string> EvaluateRevocation(ExtRevocationPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("revocation missing");
            return failures;
        }

        if (policy.SlaMinutes <= 0)
            failures.Add("revocation slaMinutes must be > 0");
        if (!policy.BreachForbiddenWithoutEscalation)
            failures.Add("breachForbiddenWithoutEscalation must be true");
        return failures;
    }

    public static string? ValidateRevocationSla(
        int elapsedMinutes,
        bool escalated,
        ExtRevocationPolicy? policy)
    {
        var sla = policy?.SlaMinutes ?? 15;
        if (elapsedMinutes > sla && (policy?.BreachForbiddenWithoutEscalation ?? true) && !escalated)
            return "revocation SLA breach without escalation forbidden";
        return null;
    }

    public static List<string> EvaluateBreakGlass(ExtBreakGlassPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("breakGlass missing");
            return failures;
        }

        if (!policy.DualControl)
            failures.Add("breakGlass.dualControl must be true");
        if (!policy.AuditRequired)
            failures.Add("breakGlass.auditRequired must be true");
        if (policy.TimeBoundMinutes <= 0)
            failures.Add("breakGlass.timeBoundMinutes must be > 0");
        return failures;
    }

    public static List<string> EvaluateSeparation(ExtCitizenEmployeeSeparation? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("citizenEmployeeSeparation missing");
            return failures;
        }

        if (!policy.Required)
            failures.Add("citizenEmployeeSeparation.required must be true");
        if (!policy.ForbidSharedPrincipal)
            failures.Add("forbidSharedPrincipal must be true");
        return failures;
    }

    public static string? ValidateCitizenEmployeeMix(
        bool sharedPrincipal,
        ExtCitizenEmployeeSeparation? policy)
    {
        if (sharedPrincipal && (policy?.ForbidSharedPrincipal ?? true))
            return "shared citizen/employee principal forbidden";
        return null;
    }

    public static List<string> EvaluateAttributeAuthority(ExtAttributeAuthority? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("attributeAuthority missing");
            return failures;
        }

        if (!policy.NoNameOnlyLinking)
            failures.Add("noNameOnlyLinking must be true");
        if (policy.ClaimsAllowlist.Count < 1)
            failures.Add("claimsAllowlist empty");
        if (!policy.ForbidLinkKeys.Any(k => string.Equals(k, "FullName", StringComparison.OrdinalIgnoreCase)))
            failures.Add("forbidLinkKeys must include FullName");
        return failures;
    }

    public static string? ValidateNameOnlyLink(string linkKey, ExtAttributeAuthority? policy)
    {
        if (!(policy?.NoNameOnlyLinking ?? true)) return null;
        if (policy?.ForbidLinkKeys.Any(k => string.Equals(k, linkKey, StringComparison.OrdinalIgnoreCase)) == true
            || string.Equals(linkKey, "FullName", StringComparison.OrdinalIgnoreCase)
            || string.Equals(linkKey, "display_name", StringComparison.OrdinalIgnoreCase))
        {
            return "name-only account linking forbidden";
        }

        return null;
    }

    public static List<string> EvaluateIdentifierPolicy(ExtIdentifierPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("identifierPolicy missing");
            return failures;
        }

        if (!policy.OpaquePreferred)
            failures.Add("opaquePreferred must be true");
        if (!policy.ForbidPlainNationalIdInLogs)
            failures.Add("forbidPlainNationalIdInLogs must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ExtFederationSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ExtFederationSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.AssurancePolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
