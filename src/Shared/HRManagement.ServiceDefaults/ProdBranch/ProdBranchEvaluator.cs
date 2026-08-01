namespace HRManagement.ServiceDefaults.ProdBranch;

public static class ProdBranchEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["journeyId", "networkProfileId", "outcome", "actor", "occurredAt", "evidenceRef"];

    public static List<string> Evaluate(ProdBranchMatrixDocument matrix, bool liveRemoteBranchClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "PROD-004", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be PROD-004, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluatePerformanceBudgets(matrix.PerformanceBudgets));
        failures.AddRange(EvaluateNetworkProfile(matrix.NetworkProfile));
        failures.AddRange(EvaluateResponsivePwa(matrix.ResponsivePwa));
        failures.AddRange(EvaluateResumable(matrix.ResumableUploads));
        failures.AddRange(EvaluateSaveDraft(matrix.SaveDraft));
        failures.AddRange(EvaluateRetry(matrix.RetryIdempotency));
        failures.AddRange(EvaluateLocalization(matrix.Localization));
        failures.AddRange(EvaluateTimezone(matrix.TimezoneCalendar));
        failures.AddRange(EvaluatePrintable(matrix.PrintableFallback));
        failures.AddRange(EvaluateOffline(matrix.Offline));
        failures.AddRange(EvaluateAccessibility(matrix.Accessibility));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.RemoteBranchPolicyVersion));

        if (liveRemoteBranchClaim)
            failures.Add("LiveRemoteBranchClaim forbidden until PROD-004a-e remote branch workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ProdBranchMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("UX-002", "Satisfied"),
                     ("DATA-001", "Satisfied"),
                     ("FILE-001", "Satisfied"),
                     ("live-remote-branch-ux", "Blocked"),
                     ("live-pwa-fleet", "Blocked"),
                     ("live-offline-field", "Blocked"),
                     ("L10N-001", "NotStarted"),
                     ("MOB-001", "NotStarted"),
                     ("PROD-005", "Satisfied"),
                     ("SYR-013", "NotStarted")
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

    public static List<string> EvaluatePerformanceBudgets(ProdBranchPerformanceBudgets? budgets)
    {
        var failures = new List<string>();
        if (budgets is null)
        {
            failures.Add("performanceBudgets missing");
            return failures;
        }

        if (!budgets.Required)
            failures.Add("performanceBudgets.required must be true");
        if (budgets.MaxTtiMs <= 0)
            failures.Add("maxTtiMs required");
        if (budgets.MaxPayloadKb <= 0)
            failures.Add("maxPayloadKb required");
        return failures;
    }

    public static string? ValidatePerformanceBudget(bool withinBudget, ProdBranchPerformanceBudgets? budgets)
    {
        if (!withinBudget && (budgets?.Required ?? true))
            return "performance budget exceeded";
        return null;
    }

    public static List<string> EvaluateNetworkProfile(ProdBranchNetworkProfile? profile)
    {
        var failures = new List<string>();
        if (profile is null)
        {
            failures.Add("networkProfile missing");
            return failures;
        }

        if (!profile.ThrottledLabRequired)
            failures.Add("throttledLabRequired must be true");
        if (!profile.LowBandwidthSupported)
            failures.Add("lowBandwidthSupported must be true");
        return failures;
    }

    public static List<string> EvaluateResponsivePwa(ProdBranchResponsivePwa? pwa)
    {
        var failures = new List<string>();
        if (pwa is null)
        {
            failures.Add("responsivePwa missing");
            return failures;
        }

        if (!pwa.Required)
            failures.Add("responsivePwa.required must be true");
        if (!pwa.LabStubAllowed)
            failures.Add("labStubAllowed must be true");
        if (!pwa.LivePwaForbidden)
            failures.Add("livePwaForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateResumable(ProdBranchResumableUploads? uploads)
    {
        if (uploads is null)
            return ["resumableUploads missing"];
        if (!uploads.Required)
            return ["resumableUploads.required must be true"];
        return [];
    }

    public static string? ValidateResumableUpload(bool resumable, ProdBranchResumableUploads? uploads)
    {
        if (!resumable && (uploads?.Required ?? true))
            return "resumable upload required";
        return null;
    }

    public static string? ValidateNoDataLossOnInterrupt(bool interrupted, bool dataPreserved)
    {
        if (interrupted && !dataPreserved)
            return "data loss on interrupt forbidden";
        return null;
    }

    public static List<string> EvaluateSaveDraft(ProdBranchSaveDraft? draft)
    {
        if (draft is null)
            return ["saveDraft missing"];
        if (!draft.Required)
            return ["saveDraft.required must be true"];
        return [];
    }

    public static string? ValidateDraftSaved(bool draftPresent, ProdBranchSaveDraft? draft)
    {
        if (!draftPresent && (draft?.Required ?? true))
            return "draft save required";
        return null;
    }

    public static List<string> EvaluateRetry(ProdBranchRetryIdempotency? retry)
    {
        var failures = new List<string>();
        if (retry is null)
        {
            failures.Add("retryIdempotency missing");
            return failures;
        }

        if (!retry.Required)
            failures.Add("retryIdempotency.required must be true");
        if (!retry.DuplicateSubmitSafe)
            failures.Add("duplicateSubmitSafe must be true");
        return failures;
    }

    public static string? ValidateIdempotentRetry(bool idempotent, ProdBranchRetryIdempotency? retry)
    {
        if (!idempotent && (retry?.DuplicateSubmitSafe ?? true))
            return "idempotent retry required";
        return null;
    }

    public static List<string> EvaluateLocalization(ProdBranchLocalization? loc)
    {
        var failures = new List<string>();
        if (loc is null)
        {
            failures.Add("localization missing");
            return failures;
        }

        if (!loc.ArabicEnglishRequired)
            failures.Add("arabicEnglishRequired must be true");
        if (!loc.RtlLtrRequired)
            failures.Add("rtlLtrRequired must be true");
        if (!loc.LocalLanguagesDecisionRequired)
            failures.Add("localLanguagesDecisionRequired must be true");
        return failures;
    }

    public static string? ValidateLocalizationReady(bool arabicEnglish, bool rtlLtr, ProdBranchLocalization? loc)
    {
        if ((!arabicEnglish || !rtlLtr) &&
            ((loc?.ArabicEnglishRequired ?? true) || (loc?.RtlLtrRequired ?? true)))
            return "localization arabic/english and RTL/LTR required";
        return null;
    }

    public static List<string> EvaluateTimezone(ProdBranchTimezoneCalendar? tz)
    {
        if (tz is null)
            return ["timezoneCalendar missing"];
        if (!tz.Required)
            return ["timezoneCalendar.required must be true"];
        return [];
    }

    public static List<string> EvaluatePrintable(ProdBranchPrintableFallback? printable)
    {
        if (printable is null)
            return ["printableFallback missing"];
        if (!printable.Required)
            return ["printableFallback.required must be true"];
        return [];
    }

    public static List<string> EvaluateOffline(ProdBranchOffline? offline)
    {
        var failures = new List<string>();
        if (offline is null)
        {
            failures.Add("offline missing");
            return failures;
        }

        if (!offline.LimitedAllowed)
            failures.Add("limitedAllowed must be true");
        if (!offline.EncryptionRequired)
            failures.Add("encryptionRequired must be true");
        if (!offline.ThreatModelRequired)
            failures.Add("threatModelRequired must be true");
        if (!offline.LiveOfflineForbidden)
            failures.Add("liveOfflineForbidden must be true");
        return failures;
    }

    public static string? ValidateOfflinePolicy(
        bool encrypted,
        bool limited,
        bool threatModelPresent,
        ProdBranchOffline? offline)
    {
        if (!encrypted && (offline?.EncryptionRequired ?? true))
            return "offline encryption required";
        if (!limited && (offline?.LimitedAllowed ?? true))
            return "offline data must be limited";
        if (!threatModelPresent && (offline?.ThreatModelRequired ?? true))
            return "offline threat model required";
        return null;
    }

    public static List<string> EvaluateAccessibility(ProdBranchAccessibility? a11y)
    {
        if (a11y is null)
            return ["accessibility missing"];
        if (!a11y.Wcag22AaRequired)
            return ["wcag22AaRequired must be true"];
        return [];
    }

    public static string? ValidateWcagRequired(bool wcag22Aa, ProdBranchAccessibility? a11y)
    {
        if (!wcag22Aa && (a11y?.Wcag22AaRequired ?? true))
            return "WCAG 2.2 AA required";
        return null;
    }

    public static List<string> EvaluateAudit(ProdBranchAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, ProdBranchAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ProdBranchMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveRemoteBranchForbidden)
            failures.Add("liveRemoteBranchForbidden must be true");
        if (!sandbox.LiveFullOfflineForbidden)
            failures.Add("liveFullOfflineForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ProdBranchSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ProdBranchSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.RemoteBranchPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
