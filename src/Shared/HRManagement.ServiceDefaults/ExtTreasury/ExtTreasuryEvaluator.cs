namespace HRManagement.ServiceDefaults.ExtTreasury;

public static class ExtTreasuryEvaluator
{
    private static readonly string[] RequiredPaymentOrderFields =
        ["orderId", "payrollRunId", "tenantId", "totalAmount", "lineCount", "idempotencyKey"];

    private static readonly string[] RequiredRunStatuses = ["Posted", "Approved"];

    private static readonly string[] RequiredAckCodes = ["ACK", "NACK"];

    private static readonly string[] RequiredRejectCodes = ["REJECT", "RETURN", "PARTIAL_REJECT"];

    private static readonly string[] RequiredSettlementStates =
        ["Submitted", "Acked", "Settled", "Rejected", "Returned"];

    private static readonly string[] RequiredManualAuditFields =
        ["fileId", "actor", "tenantId", "payrollRunId", "approvedAt"];

    private static readonly string[] RequiredAuditFields =
        ["orderId", "payrollRunId", "actor", "tenantId", "outcome", "submittedAt"];

    public static List<string> Evaluate(ExtTreasuryMatrixDocument matrix, bool liveTreasuryClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "EXT-003", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be EXT-003, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateApprovedRun(matrix));
        failures.AddRange(EvaluatePaymentOrder(matrix.PaymentOrder));
        failures.AddRange(EvaluateSignatures(matrix.Signatures));
        failures.AddRange(EvaluateChannel(matrix.Channel));
        failures.AddRange(EvaluateBankVerification(matrix.BankAccountVerification));
        failures.AddRange(EvaluateSettlement(matrix.Settlement));
        failures.AddRange(EvaluateReconciliation(matrix.Reconciliation));
        failures.AddRange(EvaluateExactlyOnce(matrix.ExactlyOnce));
        failures.AddRange(EvaluateRejectCorrection(matrix.RejectCorrectionWorkflow));
        failures.AddRange(EvaluateCutoff(matrix.CutoffCalendar));
        failures.AddRange(EvaluateManualFiles(matrix.ManualFiles));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.TreasuryPolicyVersion));

        if (liveTreasuryClaim)
            failures.Add("LiveTreasuryClaim forbidden until EXT-003a-e treasury/payment workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ExtTreasuryMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("PAY-003", "Satisfied"),
                     ("IAM-003", "Satisfied"),
                     ("INT-003", "Satisfied"),
                     ("EXT-004", "NotStarted"),
                     ("treasury-live", "Blocked"),
                     ("bank-sandbox-cert", "Blocked"),
                     ("postal-channel-live", "Blocked")
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

    public static List<string> EvaluateApprovedRun(ExtTreasuryMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (!matrix.ApprovedRunRequired)
            failures.Add("approvedRunRequired must be true");
        foreach (var need in RequiredRunStatuses)
        {
            if (!matrix.RunStatusesAllowed.Any(s => string.Equals(s, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"runStatusesAllowed missing {need}");
        }

        return failures;
    }

    public static string? ValidateApprovedRun(string runStatus, ExtTreasuryMatrixDocument matrix)
    {
        if (!matrix.ApprovedRunRequired)
            return null;
        if (!matrix.RunStatusesAllowed.Any(s => string.Equals(s, runStatus, StringComparison.OrdinalIgnoreCase)))
            return $"runStatus '{runStatus}' not in runStatusesAllowed";
        return null;
    }

    public static List<string> EvaluatePaymentOrder(ExtTreasuryPaymentOrder? order)
    {
        var failures = new List<string>();
        if (order is null)
        {
            failures.Add("paymentOrder missing");
            return failures;
        }

        if (!order.BindingToRunId)
            failures.Add("paymentOrder.bindingToRunId must be true");
        foreach (var need in RequiredPaymentOrderFields)
        {
            if (!order.RequiredFields.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"paymentOrder missing field {need}");
        }

        return failures;
    }

    public static List<string> EvaluateSignatures(ExtTreasurySignatures? signatures)
    {
        var failures = new List<string>();
        if (signatures is null)
        {
            failures.Add("signatures missing");
            return failures;
        }

        if (!signatures.LabStubAllowed)
            failures.Add("signatures.labStubAllowed must be true");
        if (!signatures.LivePkiRequiresExt004)
            failures.Add("signatures.livePkiRequiresExt004 must be true");
        return failures;
    }

    public static List<string> EvaluateChannel(ExtTreasuryChannel? channel)
    {
        var failures = new List<string>();
        if (channel is null)
        {
            failures.Add("channel missing");
            return failures;
        }

        foreach (var need in RequiredAckCodes)
        {
            if (!channel.AckCodes.Any(c => string.Equals(c, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"channel.ackCodes missing {need}");
        }

        foreach (var need in RequiredRejectCodes)
        {
            if (!channel.ReturnRejectCodes.Any(c => string.Equals(c, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"channel.returnRejectCodes missing {need}");
        }

        return failures;
    }

    public static List<string> EvaluateBankVerification(ExtTreasuryBankVerification? bank)
    {
        if (bank is null)
            return ["bankAccountVerification missing"];
        if (!bank.RequiredBeforeSettle)
            return ["bankAccountVerification.requiredBeforeSettle must be true"];
        return [];
    }

    public static List<string> EvaluateSettlement(ExtTreasurySettlement? settlement)
    {
        var failures = new List<string>();
        if (settlement is null)
        {
            failures.Add("settlement missing");
            return failures;
        }

        foreach (var need in RequiredSettlementStates)
        {
            if (!settlement.States.Any(s => string.Equals(s, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"settlement.states missing {need}");
        }

        return failures;
    }

    public static List<string> EvaluateReconciliation(ExtTreasuryReconciliation? recon)
    {
        var failures = new List<string>();
        if (recon is null)
        {
            failures.Add("reconciliation missing");
            return failures;
        }

        if (!recon.TotalsMustMatch)
            failures.Add("reconciliation.totalsMustMatch must be true");
        if (!recon.LineCountMustMatch)
            failures.Add("reconciliation.lineCountMustMatch must be true");
        return failures;
    }

    public static string? ValidateTotals(
        decimal orderTotal,
        decimal settledTotal,
        bool lineCountMatch,
        ExtTreasuryReconciliation? recon)
    {
        if (recon?.TotalsMustMatch == true && orderTotal != settledTotal)
            return "totals mismatch treated as success forbidden";
        if (recon?.LineCountMustMatch == true && !lineCountMatch)
            return "lineCount mismatch treated as success forbidden";
        return null;
    }

    public static List<string> EvaluateExactlyOnce(ExtTreasuryExactlyOnce? exactlyOnce)
    {
        var failures = new List<string>();
        if (exactlyOnce is null)
        {
            failures.Add("exactlyOnce missing");
            return failures;
        }

        if (!exactlyOnce.IdempotencyKeyRequired)
            failures.Add("exactlyOnce.idempotencyKeyRequired must be true");
        if (!string.Equals(exactlyOnce.DuplicatePolicy, "reject", StringComparison.OrdinalIgnoreCase))
            failures.Add("exactlyOnce.duplicatePolicy must be reject");
        return failures;
    }

    public static string? ValidateIdempotency(
        bool duplicateSubmit,
        bool secondBusinessEffect,
        ExtTreasuryExactlyOnce? exactlyOnce)
    {
        if (duplicateSubmit && secondBusinessEffect &&
            string.Equals(exactlyOnce?.DuplicatePolicy, "reject", StringComparison.OrdinalIgnoreCase))
            return "duplicate submit with second business effect forbidden";
        return null;
    }

    public static List<string> EvaluateRejectCorrection(ExtTreasuryRejectCorrection? workflow)
    {
        if (workflow is null)
            return ["rejectCorrectionWorkflow missing"];
        if (!workflow.Required)
            return ["rejectCorrectionWorkflow.required must be true"];
        return [];
    }

    public static string? ValidateRejectCorrection(
        bool isReject,
        bool correctionQueued,
        ExtTreasuryRejectCorrection? workflow)
    {
        if (isReject && (workflow?.Required ?? true) && !correctionQueued)
            return "reject requires correction workflow";
        return null;
    }

    public static List<string> EvaluateCutoff(ExtTreasuryCutoffCalendar? cutoff)
    {
        if (cutoff is null)
            return ["cutoffCalendar missing"];
        if (!cutoff.CutoffRequired)
            return ["cutoffCalendar.cutoffRequired must be true"];
        return [];
    }

    public static string? ValidateCutoff(bool withinCutoff, bool contingencyHold, ExtTreasuryCutoffCalendar? cutoff)
    {
        if (!withinCutoff && !contingencyHold && (cutoff?.CutoffRequired ?? true))
            return "cutoff breach without contingency forbidden";
        return null;
    }

    public static List<string> EvaluateManualFiles(ExtTreasuryManualFiles? manual)
    {
        var failures = new List<string>();
        if (manual is null)
        {
            failures.Add("manualFiles missing");
            return failures;
        }

        if (!manual.UnauditedForbidden)
            failures.Add("manualFiles.unauditedForbidden must be true");
        foreach (var need in RequiredManualAuditFields)
        {
            if (!manual.AuditRequiredFields.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"manualFiles.auditRequiredFields missing {need}");
        }

        return failures;
    }

    public static string? ValidateManualFile(bool audited, ExtTreasuryManualFiles? manual)
    {
        if (!audited && (manual?.UnauditedForbidden ?? true))
            return "unaudited manual file forbidden";
        return null;
    }

    public static List<string> EvaluateAudit(ExtTreasuryAudit? audit)
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
        ExtTreasuryAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ExtTreasuryMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveTreasuryForbidden)
            failures.Add("liveTreasuryForbidden must be true");
        return failures;
    }

    public static string? ValidateLivePkiClaim(
        bool livePki,
        ExtTreasurySignatures? signatures,
        IReadOnlyList<ExtTreasuryBlocker> blockers)
    {
        if (!livePki)
            return null;
        if (signatures?.LivePkiRequiresExt004 == true)
        {
            var ext004 = blockers.FirstOrDefault(b =>
                string.Equals(b.Id, "EXT-004", StringComparison.OrdinalIgnoreCase));
            if (ext004 is null ||
                string.Equals(ext004.Status, "NotStarted", StringComparison.OrdinalIgnoreCase))
                return "live PKI claim forbidden while EXT-004 NotStarted";
        }

        return null;
    }

    public static List<string> EvaluateSigning(ExtTreasurySigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ExtTreasurySigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.TreasuryPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
