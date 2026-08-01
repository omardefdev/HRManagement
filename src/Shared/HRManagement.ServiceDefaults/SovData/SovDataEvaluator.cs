namespace HRManagement.ServiceDefaults.SovData;

public static class SovDataEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["copyId", "classification", "location", "egressDecision", "supportTicketId", "actor", "occurredAt", "evidenceRef"];

    private static readonly string[] RequiredCopyTypes =
        ["db", "object", "backups", "logs", "traces", "ci-artifacts", "support-dumps"];

    public static List<string> Evaluate(SovDataMatrixDocument matrix, bool liveSovereigntyClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "SOV-002", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be SOV-002, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateCopyInventory(matrix.DataCopyInventory));
        failures.AddRange(EvaluateRequired(matrix.DataFlowMap, "dataFlowMap"));
        failures.AddRange(EvaluateResidency(matrix.Residency));
        failures.AddRange(EvaluateEgress(matrix.Egress));
        failures.AddRange(EvaluateRemoteSupport(matrix.RemoteSupport));
        failures.AddRange(EvaluateSubcontractors(matrix.Subcontractors));
        failures.AddRange(EvaluateTelemetry(matrix.Telemetry));
        failures.AddRange(EvaluateDestruction(matrix.Destruction));
        failures.AddRange(EvaluateLawfulDisclosure(matrix.LawfulDisclosure));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.DataSovereigntyPolicyVersion));

        if (liveSovereigntyClaim)
            failures.Add("LiveSovereigntyClaim forbidden until SOV-002a live egress enforcement");

        return failures;
    }

    public static List<string> EvaluateBlockers(SovDataMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("GOV-001", "Satisfied"),
                     ("OPS-003", "Satisfied"),
                     ("STR-002", "Satisfied"),
                     ("SOV-001", "Satisfied"),
                     ("live-egress-enforcement", "Blocked"),
                     ("live-support-jit-workers", "Blocked"),
                     ("live-deletion-certificate-system", "Blocked"),
                     ("SYR-008", "NotStarted"),
                     ("SOV-003", "Satisfied"),
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

    public static List<string> EvaluateRequired(SovDataRequiredFlag? section, string name)
    {
        if (section is null)
            return [$"{name} missing"];
        if (!section.Required)
            return [$"{name}.required must be true"];
        return [];
    }

    public static List<string> EvaluateCopyInventory(SovDataCopyInventory? inventory)
    {
        var failures = new List<string>();
        if (inventory is null)
        {
            failures.Add("dataCopyInventory missing");
            return failures;
        }

        if (!inventory.Required)
            failures.Add("dataCopyInventory.required must be true");
        if (!inventory.ClassificationRequired)
            failures.Add("classificationRequired must be true");
        if (!inventory.ApprovedLocationRequired)
            failures.Add("approvedLocationRequired must be true");
        foreach (var need in RequiredCopyTypes)
        {
            if (!inventory.CopyTypes.Any(t => string.Equals(t, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing copyType {need}");
        }

        return failures;
    }

    public static string? ValidateCopyClassification(bool classified, bool approvedLocation, SovDataCopyInventory? inventory)
    {
        if (!classified && (inventory?.ClassificationRequired ?? true))
            return "copy classification required";
        if (!approvedLocation && (inventory?.ApprovedLocationRequired ?? true))
            return "approved location required";
        return null;
    }

    public static List<string> EvaluateResidency(SovDataResidency? residency)
    {
        var failures = new List<string>();
        if (residency is null)
        {
            failures.Add("residency missing");
            return failures;
        }

        if (!residency.Required)
            failures.Add("residency.required must be true");
        if (!residency.ApprovedRegionsRequired)
            failures.Add("approvedRegionsRequired must be true");
        if (!residency.OutsideScopeForbidden)
            failures.Add("outsideScopeForbidden must be true");
        return failures;
    }

    public static string? ValidateResidency(bool insideApprovedRegion, SovDataResidency? residency)
    {
        if (!insideApprovedRegion && (residency?.OutsideScopeForbidden ?? true))
            return "copy outside approved residency scope forbidden";
        return null;
    }

    public static List<string> EvaluateEgress(SovDataEgress? egress)
    {
        var failures = new List<string>();
        if (egress is null)
        {
            failures.Add("egress missing");
            return failures;
        }

        if (!egress.Required)
            failures.Add("egress.required must be true");
        if (!egress.DenyByDefault)
            failures.Add("denyByDefault must be true");
        return failures;
    }

    public static string? ValidateEgress(bool denyByDefault, SovDataEgress? egress)
    {
        if (!denyByDefault && (egress?.DenyByDefault ?? true))
            return "egress deny-by-default required";
        return null;
    }

    public static List<string> EvaluateRemoteSupport(SovDataRemoteSupport? support)
    {
        var failures = new List<string>();
        if (support is null)
        {
            failures.Add("remoteSupport missing");
            return failures;
        }

        if (!support.Required)
            failures.Add("remoteSupport.required must be true");
        if (!support.JitRequired)
            failures.Add("jitRequired must be true");
        if (!support.LoggedRequired)
            failures.Add("loggedRequired must be true");
        if (!support.ApprovalRequired)
            failures.Add("approvalRequired must be true");
        return failures;
    }

    public static string? ValidateRemoteSupport(bool jit, bool logged, bool approved, SovDataRemoteSupport? support)
    {
        if (!jit && (support?.JitRequired ?? true))
            return "remote support JIT required";
        if (!logged && (support?.LoggedRequired ?? true))
            return "remote support must be logged";
        if (!approved && (support?.ApprovalRequired ?? true))
            return "remote support approval required";
        return null;
    }

    public static List<string> EvaluateSubcontractors(SovDataSubcontractors? subcontractors)
    {
        var failures = new List<string>();
        if (subcontractors is null)
        {
            failures.Add("subcontractors missing");
            return failures;
        }

        if (!subcontractors.Required)
            failures.Add("subcontractors.required must be true");
        if (!subcontractors.InventoryRequired)
            failures.Add("subcontractors.inventoryRequired must be true");
        return failures;
    }

    public static List<string> EvaluateTelemetry(SovDataTelemetry? telemetry)
    {
        var failures = new List<string>();
        if (telemetry is null)
        {
            failures.Add("telemetry missing");
            return failures;
        }

        if (!telemetry.Required)
            failures.Add("telemetry.required must be true");
        if (!telemetry.ResidencyBound)
            failures.Add("residencyBound must be true");
        return failures;
    }

    public static List<string> EvaluateDestruction(SovDataDestruction? destruction)
    {
        var failures = new List<string>();
        if (destruction is null)
        {
            failures.Add("destruction missing");
            return failures;
        }

        if (!destruction.Required)
            failures.Add("destruction.required must be true");
        if (!destruction.CertificateRequired)
            failures.Add("certificateRequired must be true");
        return failures;
    }

    public static string? ValidateDestruction(bool certificatePresent, SovDataDestruction? destruction)
    {
        if (!certificatePresent && (destruction?.CertificateRequired ?? true))
            return "deletion certificate required";
        return null;
    }

    public static List<string> EvaluateLawfulDisclosure(SovDataLawfulDisclosure? disclosure)
    {
        var failures = new List<string>();
        if (disclosure is null)
        {
            failures.Add("lawfulDisclosure missing");
            return failures;
        }

        if (!disclosure.Required)
            failures.Add("lawfulDisclosure.required must be true");
        if (!disclosure.LoggedDisclosureRequired)
            failures.Add("loggedDisclosureRequired must be true");
        return failures;
    }

    public static string? ValidateLawfulDisclosure(bool logged, SovDataLawfulDisclosure? disclosure)
    {
        if (!logged && (disclosure?.LoggedDisclosureRequired ?? true))
            return "lawful disclosure must be logged";
        return null;
    }

    public static string? ValidateDataFlowMap(bool mapPresent, SovDataRequiredFlag? map)
    {
        if (!mapPresent && (map?.Required ?? true))
            return "data flow map required";
        return null;
    }

    public static List<string> EvaluateAudit(SovDataAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, SovDataAudit? audit)
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

    public static List<string> EvaluateMockSandbox(SovDataMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveEgressForbidden)
            failures.Add("liveEgressForbidden must be true");
        if (!sandbox.LiveSupportJitForbidden)
            failures.Add("liveSupportJitForbidden must be true");
        if (!sandbox.LiveDeletionSystemForbidden)
            failures.Add("liveDeletionSystemForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(SovDataSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(SovDataSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.DataSovereigntyPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
