namespace HRManagement.ServiceDefaults.ExtPki;

public static class ExtPkiEvaluator
{
    private static readonly string[] RequiredSignatureKinds = ["document", "approval"];
    private static readonly string[] RequiredRevocationSources = ["CRL", "OCSP"];
    private static readonly string[] RequiredAllowedAlgs = ["RSA-PSS-SHA256", "ECDSA-P256-SHA256"];
    private static readonly string[] RequiredDeprecatedAlgs = ["MD5", "SHA1", "RSA-PKCS1-SHA1"];
    private static readonly string[] RequiredAuditFields =
        ["signatureId", "signerId", "signerRole", "outcome", "algorithm", "signedAt"];

    public static List<string> Evaluate(ExtPkiMatrixDocument matrix, bool livePkiClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "EXT-004", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be EXT-004, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateSignerIntent(matrix));
        failures.AddRange(EvaluateSignatures(matrix.Signatures));
        failures.AddRange(EvaluateOrganizationalSeals(matrix.OrganizationalSeals));
        failures.AddRange(EvaluateTrustedTimestamp(matrix.TrustedTimestamp));
        failures.AddRange(EvaluateCertValidation(matrix.CertValidation));
        failures.AddRange(EvaluateRevocation(matrix.Revocation));
        failures.AddRange(EvaluateLtv(matrix.Ltv));
        failures.AddRange(EvaluateHsm(matrix.HsmCustody));
        failures.AddRange(EvaluateClockSkew(matrix.ClockSkewPolicy));
        failures.AddRange(EvaluateAlgorithmAgility(matrix.AlgorithmAgility));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.PkiPolicyVersion));

        if (livePkiClaim)
            failures.Add("LivePkiClaim forbidden until EXT-004a-f PKI workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ExtPkiMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("STR-002", "Satisfied"),
                     ("OBS-003", "Satisfied"),
                     ("SOV-003", "Satisfied"),
                     ("national-pki-live", "Blocked"),
                     ("hsm-prod", "Blocked"),
                     ("tsa-live", "Blocked"),
                     ("SYD-011", "Blocked")
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

    public static List<string> EvaluateSignerIntent(ExtPkiMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (!matrix.SignerIntentRequired)
            failures.Add("signerIntentRequired must be true");
        if (!matrix.SignerRoleRequired)
            failures.Add("signerRoleRequired must be true");
        return failures;
    }

    public static string? ValidateSignerIntent(
        bool intentPresent,
        bool rolePresent,
        ExtPkiMatrixDocument matrix)
    {
        if (matrix.SignerIntentRequired && !intentPresent)
            return "signer intent required";
        if (matrix.SignerRoleRequired && !rolePresent)
            return "signer role required";
        return null;
    }

    public static List<string> EvaluateSignatures(ExtPkiSignatures? signatures)
    {
        var failures = new List<string>();
        if (signatures is null)
        {
            failures.Add("signatures missing");
            return failures;
        }

        if (!signatures.FormSignatureNotLegalEvidence)
            failures.Add("formSignatureNotLegalEvidence must be true");
        foreach (var need in RequiredSignatureKinds)
        {
            if (!signatures.Kinds.Any(k => string.Equals(k, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"signatures.kinds missing {need}");
        }

        return failures;
    }

    public static string? ValidateFormSignatureAsLegal(bool treatedAsLegal, ExtPkiSignatures? signatures)
    {
        if (treatedAsLegal && (signatures?.FormSignatureNotLegalEvidence ?? true))
            return "form signature as legal evidence forbidden";
        return null;
    }

    public static List<string> EvaluateOrganizationalSeals(ExtPkiOrganizationalSeals? seals)
    {
        var failures = new List<string>();
        if (seals is null)
        {
            failures.Add("organizationalSeals missing");
            return failures;
        }

        if (!seals.CryptoSealRequiredForLegal)
            failures.Add("cryptoSealRequiredForLegal must be true");
        if (!seals.MetadataOnlyInsufficient)
            failures.Add("metadataOnlyInsufficient must be true");
        return failures;
    }

    public static string? ValidateOrgSeal(
        bool cryptoSealPresent,
        bool metadataOnly,
        ExtPkiOrganizationalSeals? seals)
    {
        if (metadataOnly && !cryptoSealPresent && (seals?.MetadataOnlyInsufficient ?? true))
            return "metadata-only seal insufficient for legal evidence";
        if (!cryptoSealPresent && (seals?.CryptoSealRequiredForLegal ?? true))
            return "crypto seal required for legal evidence";
        return null;
    }

    public static List<string> EvaluateTrustedTimestamp(ExtPkiTrustedTimestamp? timestamp)
    {
        var failures = new List<string>();
        if (timestamp is null)
        {
            failures.Add("trustedTimestamp missing");
            return failures;
        }

        if (!timestamp.RequiredForLtv)
            failures.Add("trustedTimestamp.requiredForLtv must be true");
        if (!string.Equals(timestamp.TsaOutageDegrade, "hold-not-valid", StringComparison.OrdinalIgnoreCase))
            failures.Add("tsaOutageDegrade must be hold-not-valid");
        return failures;
    }

    public static string? ValidateTsaOutage(
        bool tsaAvailable,
        bool treatedAsValid,
        ExtPkiTrustedTimestamp? timestamp)
    {
        if (!tsaAvailable && treatedAsValid &&
            string.Equals(timestamp?.TsaOutageDegrade, "hold-not-valid", StringComparison.OrdinalIgnoreCase))
            return "TSA outage must not be treated as valid";
        return null;
    }

    public static List<string> EvaluateCertValidation(ExtPkiCertValidation? cert)
    {
        var failures = new List<string>();
        if (cert is null)
        {
            failures.Add("certValidation missing");
            return failures;
        }

        if (!cert.CheckExpiry)
            failures.Add("certValidation.checkExpiry must be true");
        if (!cert.CheckRevocation)
            failures.Add("certValidation.checkRevocation must be true");
        if (!cert.FailClosedOnUnknown)
            failures.Add("certValidation.failClosedOnUnknown must be true");
        return failures;
    }

    public static List<string> EvaluateRevocation(ExtPkiRevocation? revocation)
    {
        var failures = new List<string>();
        if (revocation is null)
        {
            failures.Add("revocation missing");
            return failures;
        }

        if (!revocation.FailClosed)
            failures.Add("revocation.failClosed must be true");
        foreach (var need in RequiredRevocationSources)
        {
            if (!revocation.Sources.Any(s => string.Equals(s, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"revocation.sources missing {need}");
        }

        return failures;
    }

    public static string? ValidateRevocation(bool revoked, bool treatedAsValid, ExtPkiRevocation? revocation)
    {
        if (revoked && treatedAsValid && (revocation?.FailClosed ?? true))
            return "revoked cert treated as valid forbidden";
        return null;
    }

    public static List<string> EvaluateLtv(ExtPkiLtv? ltv)
    {
        var failures = new List<string>();
        if (ltv is null)
        {
            failures.Add("ltv missing");
            return failures;
        }

        if (!ltv.RequiredAfterExpiry)
            failures.Add("ltv.requiredAfterExpiry must be true");
        if (!ltv.ArchiveEvidenceRequired)
            failures.Add("ltv.archiveEvidenceRequired must be true");
        return failures;
    }

    public static string? ValidateExpiredLtv(
        bool expired,
        bool hasLtv,
        bool treatedAsValid,
        ExtPkiLtv? ltv)
    {
        if (expired && !hasLtv && treatedAsValid && (ltv?.RequiredAfterExpiry ?? true))
            return "expired cert without LTV treated as valid forbidden";
        return null;
    }

    public static List<string> EvaluateHsm(ExtPkiHsmCustody? hsm)
    {
        var failures = new List<string>();
        if (hsm is null)
        {
            failures.Add("hsmCustody missing");
            return failures;
        }

        if (!hsm.PrivateKeyNeverLeavesHsm)
            failures.Add("privateKeyNeverLeavesHsm must be true");
        if (!hsm.LabMockAllowed)
            failures.Add("hsmCustody.labMockAllowed must be true");
        return failures;
    }

    public static string? ValidateKeyExfil(bool keyLeftHsm, ExtPkiHsmCustody? hsm)
    {
        if (keyLeftHsm && (hsm?.PrivateKeyNeverLeavesHsm ?? true))
            return "private key leaving HSM forbidden";
        return null;
    }

    public static List<string> EvaluateClockSkew(ExtPkiClockSkewPolicy? clock)
    {
        var failures = new List<string>();
        if (clock is null)
        {
            failures.Add("clockSkewPolicy missing");
            return failures;
        }

        if (!clock.FailClosed)
            failures.Add("clockSkewPolicy.failClosed must be true");
        if (clock.MaxSkewSeconds <= 0)
            failures.Add("maxSkewSeconds must be > 0");
        return failures;
    }

    public static string? ValidateClockSkew(int skewSeconds, ExtPkiClockSkewPolicy? clock)
    {
        if (clock is null)
            return null;
        if (Math.Abs(skewSeconds) > clock.MaxSkewSeconds && clock.FailClosed)
            return "clock skew beyond maxSkewSeconds forbidden";
        return null;
    }

    public static List<string> EvaluateAlgorithmAgility(ExtPkiAlgorithmAgility? alg)
    {
        var failures = new List<string>();
        if (alg is null)
        {
            failures.Add("algorithmAgility missing");
            return failures;
        }

        if (alg.AllowedAlgs.Count < 1)
            failures.Add("allowedAlgs empty");
        foreach (var need in RequiredAllowedAlgs)
        {
            if (!alg.AllowedAlgs.Any(a => string.Equals(a, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"allowedAlgs missing {need}");
        }

        foreach (var need in RequiredDeprecatedAlgs)
        {
            if (!alg.DeprecatedAlgs.Any(a => string.Equals(a, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"deprecatedAlgs missing {need}");
        }

        return failures;
    }

    public static string? ValidateAlgorithm(string algorithm, ExtPkiAlgorithmAgility? alg)
    {
        if (alg is null)
            return null;
        if (alg.DeprecatedAlgs.Any(a => string.Equals(a, algorithm, StringComparison.OrdinalIgnoreCase)))
            return $"deprecated algorithm '{algorithm}' forbidden";
        if (alg.AllowedAlgs.Count > 0 &&
            !alg.AllowedAlgs.Any(a => string.Equals(a, algorithm, StringComparison.OrdinalIgnoreCase)))
            return $"algorithm '{algorithm}' not in allowedAlgs";
        return null;
    }

    public static List<string> EvaluateAudit(ExtPkiAudit? audit)
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
        ExtPkiAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ExtPkiMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LivePkiForbidden)
            failures.Add("livePkiForbidden must be true");
        if (!sandbox.MockCa)
            failures.Add("mockSandbox.mockCa must be true");
        if (!sandbox.MockTsa)
            failures.Add("mockSandbox.mockTsa must be true");
        if (!sandbox.MockHsm)
            failures.Add("mockSandbox.mockHsm must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ExtPkiSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ExtPkiSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.PkiPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
