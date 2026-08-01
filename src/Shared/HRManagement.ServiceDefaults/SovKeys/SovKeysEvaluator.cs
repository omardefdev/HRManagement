namespace HRManagement.ServiceDefaults.SovKeys;

public static class SovKeysEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["keyId", "purpose", "custodyType", "actor", "occurredAt", "evidenceRef", "outcome", "ceremonyRef"];

    public static List<string> Evaluate(SovKeysMatrixDocument matrix, bool liveKeyCustodyClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "SOV-003", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be SOV-003, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateHierarchy(matrix.KeyHierarchy));
        failures.AddRange(EvaluateHsmKms(matrix.HsmKms));
        failures.AddRange(EvaluateByokHyok(matrix.ByokHyok));
        failures.AddRange(EvaluateSeparation(matrix.Separation));
        failures.AddRange(EvaluateRequired(matrix.DualControl, "dualControl"));
        failures.AddRange(EvaluateRotation(matrix.Rotation));
        failures.AddRange(EvaluateAlgorithmAgility(matrix.AlgorithmAgility));
        failures.AddRange(EvaluateTlsCertificates(matrix.TlsCertificates));
        failures.AddRange(EvaluateTrustedTime(matrix.TrustedTime));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.KeySovereigntyPolicyVersion));

        if (liveKeyCustodyClaim)
            failures.Add("LiveKeyCustodyClaim forbidden until SOV-003a live HSM/KMS");

        return failures;
    }

    public static List<string> EvaluateBlockers(SovKeysMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("OPS-002", "Satisfied"),
                     ("STR-002", "Satisfied"),
                     ("SOV-002", "Satisfied"),
                     ("SEC-004", "Blocked"),
                     ("live-hsm-prod", "Blocked"),
                     ("live-key-ceremony-workers", "Blocked"),
                     ("live-trusted-time", "Blocked"),
                     ("EXT-004", "Satisfied"),
                     ("SOV-004", "Satisfied"),
                     ("SIG-001", "NotStarted"),
                     ("NAT-001", "NotStarted")
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

    public static List<string> EvaluateRequired(SovKeysRequiredFlag? section, string name)
    {
        if (section is null)
            return [$"{name} missing"];
        if (!section.Required)
            return [$"{name}.required must be true"];
        return [];
    }

    public static List<string> EvaluateHierarchy(SovKeysHierarchy? hierarchy)
    {
        var failures = new List<string>();
        if (hierarchy is null)
        {
            failures.Add("keyHierarchy missing");
            return failures;
        }

        if (!hierarchy.Required)
            failures.Add("keyHierarchy.required must be true");
        if (!hierarchy.InventoryRequired)
            failures.Add("inventoryRequired must be true");
        return failures;
    }

    public static string? ValidateHierarchy(bool inventoryPresent, SovKeysHierarchy? hierarchy)
    {
        if (!inventoryPresent && (hierarchy?.InventoryRequired ?? true))
            return "key hierarchy inventory required";
        return null;
    }

    public static List<string> EvaluateHsmKms(SovKeysHsmKms? hsm)
    {
        var failures = new List<string>();
        if (hsm is null)
        {
            failures.Add("hsmKms missing");
            return failures;
        }

        if (!hsm.Required)
            failures.Add("hsmKms.required must be true");
        if (!hsm.ApprovedCustodyRequired)
            failures.Add("approvedCustodyRequired must be true");
        if (!hsm.VendorAloneCannotDecrypt)
            failures.Add("vendorAloneCannotDecrypt must be true");
        if (!hsm.LabMockAllowed)
            failures.Add("labMockAllowed must be true");
        return failures;
    }

    public static string? ValidateVendorAloneCannotDecrypt(bool vendorAlone, SovKeysHsmKms? hsm)
    {
        if (vendorAlone && (hsm?.VendorAloneCannotDecrypt ?? true))
            return "vendor alone decrypt forbidden";
        return null;
    }

    public static string? ValidateSoftKeyProdClaim(bool softKeyClaimedAsProd)
    {
        if (softKeyClaimedAsProd)
            return "soft-key production custody claim forbidden";
        return null;
    }

    public static List<string> EvaluateByokHyok(SovKeysByokHyok? byok)
    {
        var failures = new List<string>();
        if (byok is null)
        {
            failures.Add("byokHyok missing");
            return failures;
        }

        if (!byok.Required)
            failures.Add("byokHyok.required must be true");
        if (!byok.WhereRequiredFlag)
            failures.Add("whereRequiredFlag must be true");
        return failures;
    }

    public static List<string> EvaluateSeparation(SovKeysSeparation? separation)
    {
        var failures = new List<string>();
        if (separation is null)
        {
            failures.Add("separation missing");
            return failures;
        }

        if (!separation.Required)
            failures.Add("separation.required must be true");
        if (!separation.EnvTenantPurposeSeparation)
            failures.Add("envTenantPurposeSeparation must be true");
        return failures;
    }

    public static string? ValidateDualControl(bool dualControlPresent, SovKeysRequiredFlag? dualControl)
    {
        if (!dualControlPresent && (dualControl?.Required ?? true))
            return "dual control required";
        return null;
    }

    public static List<string> EvaluateRotation(SovKeysRotation? rotation)
    {
        var failures = new List<string>();
        if (rotation is null)
        {
            failures.Add("rotation missing");
            return failures;
        }

        if (!rotation.Required)
            failures.Add("rotation.required must be true");
        if (!rotation.RevocationRequired)
            failures.Add("revocationRequired must be true");
        if (!rotation.BackupRequired)
            failures.Add("backupRequired must be true");
        if (!rotation.DestructionRequired)
            failures.Add("destructionRequired must be true");
        if (!rotation.CustodianLossDrillRequired)
            failures.Add("custodianLossDrillRequired must be true");
        return failures;
    }

    public static string? ValidateRotation(bool rotationDrillPresent, SovKeysRotation? rotation)
    {
        if (!rotationDrillPresent && (rotation?.CustodianLossDrillRequired ?? true))
            return "rotation/custodian-loss drill required";
        return null;
    }

    public static List<string> EvaluateAlgorithmAgility(SovKeysAlgorithmAgility? agility)
    {
        var failures = new List<string>();
        if (agility is null)
        {
            failures.Add("algorithmAgility missing");
            return failures;
        }

        if (!agility.Required)
            failures.Add("algorithmAgility.required must be true");
        if (!agility.DeprecatedAlgForbiddenInProdClaim)
            failures.Add("deprecatedAlgForbiddenInProdClaim must be true");
        return failures;
    }

    public static string? ValidateAlgorithmAgility(bool deprecatedAlgClaimedInProd, SovKeysAlgorithmAgility? agility)
    {
        if (deprecatedAlgClaimedInProd && (agility?.DeprecatedAlgForbiddenInProdClaim ?? true))
            return "deprecated algorithm production claim forbidden";
        return null;
    }

    public static List<string> EvaluateTlsCertificates(SovKeysTlsCertificates? tls)
    {
        var failures = new List<string>();
        if (tls is null)
        {
            failures.Add("tlsCertificates missing");
            return failures;
        }

        if (!tls.Required)
            failures.Add("tlsCertificates.required must be true");
        if (!tls.InventoryExpiryAlertsRequired)
            failures.Add("inventoryExpiryAlertsRequired must be true");
        return failures;
    }

    public static List<string> EvaluateTrustedTime(SovKeysTrustedTime? time)
    {
        var failures = new List<string>();
        if (time is null)
        {
            failures.Add("trustedTime missing");
            return failures;
        }

        if (!time.Required)
            failures.Add("trustedTime.required must be true");
        if (!time.ClockDriftBoundRequired)
            failures.Add("clockDriftBoundRequired must be true");
        return failures;
    }

    public static string? ValidateTrustedTime(bool clockDriftBoundPresent, SovKeysTrustedTime? time)
    {
        if (!clockDriftBoundPresent && (time?.ClockDriftBoundRequired ?? true))
            return "clock drift bound required";
        return null;
    }

    public static List<string> EvaluateAudit(SovKeysAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, SovKeysAudit? audit)
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

    public static List<string> EvaluateMockSandbox(SovKeysMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveHsmForbidden)
            failures.Add("liveHsmForbidden must be true");
        if (!sandbox.LiveKeyCeremonyForbidden)
            failures.Add("liveKeyCeremonyForbidden must be true");
        if (!sandbox.LiveTrustedTimeForbidden)
            failures.Add("liveTrustedTimeForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(SovKeysSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(SovKeysSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.KeySovereigntyPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
