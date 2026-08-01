namespace HRManagement.ServiceDefaults.ExtSector;

public static class ExtSectorEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["adapterId", "domain", "outcome", "actor", "occurredAt", "evidenceRef"];

    private static readonly string[] RequiredDomains =
        ["pension", "insurance", "tax", "attendance", "erp", "budget", "mail-notify"];

    public static List<string> Evaluate(ExtSectorMatrixDocument matrix, bool liveCatalogClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "EXT-007", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be EXT-007, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        if (!matrix.NoImplicitCriticalIntegrations)
            failures.Add("noImplicitCriticalIntegrations must be true");
        failures.AddRange(EvaluateAdapterRequirements(matrix.AdapterRequirements));
        failures.AddRange(EvaluateNotification(matrix.Notification));
        failures.AddRange(EvaluateFailure(matrix.Failure));
        failures.AddRange(EvaluateCertification(matrix.Certification));
        failures.AddRange(EvaluateReconciliation(matrix.Reconciliation));
        failures.AddRange(EvaluateDeliveryReceipts(matrix.DeliveryReceipts));
        failures.AddRange(EvaluateCatalogDomains(matrix.CatalogDomains));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.SectorCatalogPolicyVersion));

        if (liveCatalogClaim)
            failures.Add("LiveSectorCatalogClaim forbidden until EXT-007a-e sector catalog workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ExtSectorMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("EXT-005", "Satisfied"),
                     ("STR-003", "Satisfied"),
                     ("live-sector-adapters", "Blocked"),
                     ("live-notification-channel", "Blocked"),
                     ("adapter-fleet-certified-live", "Blocked"),
                     ("PROD-005", "Satisfied"),
                     ("COM-001", "NotStarted")
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

    public static List<string> EvaluateAdapterRequirements(ExtSectorAdapterRequirements? req)
    {
        var failures = new List<string>();
        if (req is null)
        {
            failures.Add("adapterRequirements missing");
            return failures;
        }

        if (!req.ContractRequired) failures.Add("contractRequired must be true");
        if (!req.SloRequired) failures.Add("sloRequired must be true");
        if (!req.RetryRequired) failures.Add("retryRequired must be true");
        if (!req.ReconciliationRequired) failures.Add("reconciliationRequired must be true");
        if (!req.ExitRequired) failures.Add("exitRequired must be true");
        return failures;
    }

    public static string? ValidateNoImplicitCritical(bool isDeclaredCritical, bool isInCatalog, bool noImplicit)
    {
        if (isDeclaredCritical && !isInCatalog && noImplicit)
            return "critical integration must be declared in catalog";
        return null;
    }

    public static string? ValidateAdapterCompleteness(
        bool hasContract, bool hasSlo, bool hasRetry, bool hasRecon, bool hasExit,
        ExtSectorAdapterRequirements? req)
    {
        if (!(req?.ContractRequired ?? true) && !(req?.SloRequired ?? true))
            return null;
        if (!hasContract && (req?.ContractRequired ?? true))
            return "adapter contract required";
        if (!hasSlo && (req?.SloRequired ?? true))
            return "adapter SLO required";
        if (!hasRetry && (req?.RetryRequired ?? true))
            return "adapter retry required";
        if (!hasRecon && (req?.ReconciliationRequired ?? true))
            return "adapter reconciliation required";
        if (!hasExit && (req?.ExitRequired ?? true))
            return "adapter exit required";
        return null;
    }

    public static List<string> EvaluateNotification(ExtSectorNotification? notification)
    {
        if (notification is null)
            return ["notification missing"];
        if (!notification.SensitivePiiForbidden)
            return ["sensitivePiiForbidden must be true"];
        return [];
    }

    public static string? ValidateNotificationNoSensitivePii(bool containsSensitivePii, bool forbidden)
    {
        if (containsSensitivePii && forbidden)
            return "notification sensitive PII forbidden";
        return null;
    }

    public static List<string> EvaluateFailure(ExtSectorFailure? failure)
    {
        var failures = new List<string>();
        if (failure is null)
        {
            failures.Add("failure missing");
            return failures;
        }

        if (!failure.MustSurface) failures.Add("mustSurface must be true");
        if (!failure.SwallowForbidden) failures.Add("swallowForbidden must be true");
        return failures;
    }

    public static string? ValidateFailureSurfaced(bool failureOccurred, bool wasSwallowed, ExtSectorFailure? failure)
    {
        if (failureOccurred && wasSwallowed && (failure?.SwallowForbidden ?? true))
            return "swallowed failure forbidden";
        if (failureOccurred && wasSwallowed && (failure?.MustSurface ?? true))
            return "failure must surface";
        return null;
    }

    public static List<string> EvaluateCertification(ExtSectorCertification? certification)
    {
        if (certification is null)
            return ["certification missing"];
        if (!certification.RequiredBeforeEnable)
            return ["requiredBeforeEnable must be true"];
        return [];
    }

    public static string? ValidateCertification(bool enabled, bool certified, ExtSectorCertification? certification)
    {
        if (enabled && !certified && (certification?.RequiredBeforeEnable ?? true))
            return "uncertified adapter enable forbidden";
        return null;
    }

    public static List<string> EvaluateReconciliation(ExtSectorReconciliation? reconciliation)
    {
        if (reconciliation is null)
            return ["reconciliation missing"];
        if (!reconciliation.Required)
            return ["reconciliation.required must be true"];
        return [];
    }

    public static string? ValidateReconciliation(bool required, bool completed, ExtSectorReconciliation? policy)
    {
        if ((required || (policy?.Required ?? true)) && !completed)
            return "reconciliation required but not completed";
        return null;
    }

    public static List<string> EvaluateDeliveryReceipts(ExtSectorDeliveryReceipts? receipts)
    {
        if (receipts is null)
            return ["deliveryReceipts missing"];
        if (!receipts.RequiredWhereApplicable)
            return ["deliveryReceipts.requiredWhereApplicable must be true"];
        return [];
    }

    public static string? ValidateDeliveryReceipt(bool applicable, bool hasReceipt, ExtSectorDeliveryReceipts? policy)
    {
        if (applicable && !hasReceipt && (policy?.RequiredWhereApplicable ?? true))
            return "delivery receipt required where applicable";
        return null;
    }

    public static List<string> EvaluateCatalogDomains(List<ExtSectorCatalogDomain> domains)
    {
        var failures = new List<string>();
        var ids = domains.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in RequiredDomains)
        {
            if (!ids.Contains(need))
                failures.Add($"missing catalogDomain {need}");
        }

        foreach (var d in domains)
        {
            if (string.IsNullOrWhiteSpace(d.Priority)) failures.Add($"catalogDomain {d.Id} missing priority");
            if (string.IsNullOrWhiteSpace(d.SourceOfTruth)) failures.Add($"catalogDomain {d.Id} missing sourceOfTruth");
            if (string.IsNullOrWhiteSpace(d.LatencyClass)) failures.Add($"catalogDomain {d.Id} missing latencyClass");
            if (string.IsNullOrWhiteSpace(d.OwnerRole)) failures.Add($"catalogDomain {d.Id} missing ownerRole");
            if (string.IsNullOrWhiteSpace(d.Fallback)) failures.Add($"catalogDomain {d.Id} missing fallback");
        }

        return failures;
    }

    public static List<string> EvaluateAudit(ExtSectorAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, ExtSectorAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ExtSectorMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveAdaptersForbidden)
            failures.Add("liveAdaptersForbidden must be true");
        if (!sandbox.LiveNotificationForbidden)
            failures.Add("liveNotificationForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ExtSectorSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ExtSectorSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.SectorCatalogPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
