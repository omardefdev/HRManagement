namespace HRManagement.ServiceDefaults.ProdWorkbox;

public static class ProdWorkboxEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["itemId", "actorRole", "tenantId", "outcome", "actor", "occurredAt", "evidenceRef"];

    private static readonly string[] RequiredItemTypes = ["tasks", "cases", "approvals"];

    public static List<string> Evaluate(ProdWorkboxMatrixDocument matrix, bool liveWorkboxClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "PROD-005", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be PROD-005, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateQueue(matrix.QueueCompleteness));
        failures.AddRange(EvaluatePriority(matrix.PrioritySlaEscalation));
        failures.AddRange(EvaluateSafeSearch(matrix.SafeSearch));
        failures.AddRange(EvaluateBulk(matrix.BulkActions));
        failures.AddRange(EvaluateNotifications(matrix.Notifications));
        failures.AddRange(EvaluateDeepLinks(matrix.DeepLinks));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.WorkboxPolicyVersion));

        if (liveWorkboxClaim)
            failures.Add("LiveWorkboxClaim forbidden until PROD-005a-e workbox workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ProdWorkboxMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("INT-003", "Satisfied"),
                     ("EXT-007", "Satisfied"),
                     ("IAM-002", "NotStarted"),
                     ("live-workbox-ui", "Blocked"),
                     ("live-notification-delivery", "Blocked"),
                     ("live-safe-search", "Blocked"),
                     ("COM-001", "NotStarted"),
                     ("WF-002", "NotStarted"),
                     ("PROD-006", "Satisfied"),
                     ("NAT-005", "NotStarted")
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

    public static List<string> EvaluateQueue(ProdWorkboxQueueCompleteness? queue)
    {
        var failures = new List<string>();
        if (queue is null)
        {
            failures.Add("queueCompleteness missing");
            return failures;
        }

        if (!queue.Required)
            failures.Add("queueCompleteness.required must be true");
        if (!queue.NoLostCrossModule)
            failures.Add("noLostCrossModule must be true");
        foreach (var need in RequiredItemTypes)
        {
            if (!queue.ItemTypes.Any(t => string.Equals(t, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing itemType {need}");
        }

        return failures;
    }

    public static string? ValidateNoLostTransaction(bool persistedAcrossModules, ProdWorkboxQueueCompleteness? queue)
    {
        if (!persistedAcrossModules && (queue?.NoLostCrossModule ?? true))
            return "cross-module transaction must persist";
        return null;
    }

    public static List<string> EvaluatePriority(ProdWorkboxPrioritySlaEscalation? priority)
    {
        if (priority is null)
            return ["prioritySlaEscalation missing"];
        if (!priority.Required)
            return ["prioritySlaEscalation.required must be true"];
        return [];
    }

    public static string? ValidateEscalationTracked(bool escalationPresent, ProdWorkboxPrioritySlaEscalation? priority)
    {
        if (!escalationPresent && (priority?.Required ?? true))
            return "escalation tracking required";
        return null;
    }

    public static List<string> EvaluateSafeSearch(ProdWorkboxSafeSearch? search)
    {
        var failures = new List<string>();
        if (search is null)
        {
            failures.Add("safeSearch missing");
            return failures;
        }

        if (!search.Required)
            failures.Add("safeSearch.required must be true");
        if (!search.TenantScoped)
            failures.Add("tenantScoped must be true");
        if (!search.OrgScoped)
            failures.Add("orgScoped must be true");
        if (!search.ExistenceLeakForbidden)
            failures.Add("existenceLeakForbidden must be true");
        return failures;
    }

    public static string? ValidateSafeSearch(
        bool withinTenant,
        bool withinOrg,
        bool existenceLeakAttempted,
        ProdWorkboxSafeSearch? search)
    {
        if ((!withinTenant || !withinOrg) && (search?.TenantScoped ?? true))
            return "search must stay within tenant/org scope";
        if (existenceLeakAttempted && (search?.ExistenceLeakForbidden ?? true))
            return "existence leak via search forbidden";
        return null;
    }

    public static List<string> EvaluateBulk(ProdWorkboxBulkActions? bulk)
    {
        var failures = new List<string>();
        if (bulk is null)
        {
            failures.Add("bulkActions missing");
            return failures;
        }

        if (!bulk.Required)
            failures.Add("bulkActions.required must be true");
        if (!bulk.LimitsRequired)
            failures.Add("limitsRequired must be true");
        if (!bulk.FourEyesRequired)
            failures.Add("fourEyesRequired must be true");
        return failures;
    }

    public static string? ValidateBulkApprove(bool withinLimits, bool fourEyesSatisfied, ProdWorkboxBulkActions? bulk)
    {
        if (!withinLimits && (bulk?.LimitsRequired ?? true))
            return "bulk approve limits required";
        if (!fourEyesSatisfied && (bulk?.FourEyesRequired ?? true))
            return "bulk approve four-eyes required";
        return null;
    }

    public static List<string> EvaluateNotifications(ProdWorkboxNotifications? notifications)
    {
        var failures = new List<string>();
        if (notifications is null)
        {
            failures.Add("notifications missing");
            return failures;
        }

        if (!notifications.Required)
            failures.Add("notifications.required must be true");
        if (!notifications.NoSecretsInPayload)
            failures.Add("noSecretsInPayload must be true");
        if (!notifications.DeliveryReceiptsRequired)
            failures.Add("deliveryReceiptsRequired must be true");
        if (!notifications.LabStubAllowed)
            failures.Add("labStubAllowed must be true");
        if (!notifications.LiveForbidden)
            failures.Add("notifications.liveForbidden must be true");
        return failures;
    }

    public static string? ValidateNotificationSafe(
        bool containsSecret,
        bool hasDeliveryReceipt,
        bool leadsToCorrectState,
        ProdWorkboxNotifications? notifications)
    {
        if (containsSecret && (notifications?.NoSecretsInPayload ?? true))
            return "notification must not contain secrets";
        if (!hasDeliveryReceipt && (notifications?.DeliveryReceiptsRequired ?? true))
            return "delivery receipt required";
        if (!leadsToCorrectState)
            return "notification must lead to correct state";
        return null;
    }

    public static List<string> EvaluateDeepLinks(ProdWorkboxDeepLinks? links)
    {
        var failures = new List<string>();
        if (links is null)
        {
            failures.Add("deepLinks missing");
            return failures;
        }

        if (!links.Required)
            failures.Add("deepLinks.required must be true");
        if (!links.ReauthOnExpiredRequired)
            failures.Add("reauthOnExpiredRequired must be true");
        if (!links.CorrectStateRequired)
            failures.Add("correctStateRequired must be true");
        return failures;
    }

    public static string? ValidateDeepLink(bool expired, bool reauthPerformed, ProdWorkboxDeepLinks? links)
    {
        if (expired && !reauthPerformed && (links?.ReauthOnExpiredRequired ?? true))
            return "expired deep link requires reauth";
        return null;
    }

    public static List<string> EvaluateAudit(ProdWorkboxAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, ProdWorkboxAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ProdWorkboxMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveWorkboxForbidden)
            failures.Add("liveWorkboxForbidden must be true");
        if (!sandbox.LiveNotificationForbidden)
            failures.Add("liveNotificationForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ProdWorkboxSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ProdWorkboxSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.WorkboxPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
