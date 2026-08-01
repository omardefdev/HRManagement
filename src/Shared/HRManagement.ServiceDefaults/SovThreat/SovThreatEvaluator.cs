namespace HRManagement.ServiceDefaults.SovThreat;

public static class SovThreatEvaluator
{
    private static readonly string[] RequiredAuditFields =
    [
        "threatId", "category", "severity", "controlRef", "testRef", "ownerRole", "actor", "occurredAt",
        "evidenceRef", "outcome"
    ];

    private static readonly string[] RequiredCategories =
    [
        "payrollFraud", "insider", "massExport", "tenantEscape", "messageReplay", "supplyChain", "migration",
        "recovery"
    ];

    public static List<string> Evaluate(SovThreatMatrixDocument matrix, bool liveThreatModelClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "SOV-005", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be SOV-005, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateMethodology(matrix.Methodology));
        failures.AddRange(EvaluateTrustBoundaries(matrix.TrustBoundaries));
        failures.AddRange(EvaluateRequired(matrix.AssetsActors, "assetsActors"));
        failures.AddRange(EvaluateCategories(matrix.ThreatCategories));
        failures.AddRange(EvaluateHighCoverage(matrix.HighThreatCoverage));
        failures.AddRange(EvaluateChangeTrigger(matrix.ChangeTrigger));
        failures.AddRange(EvaluateMisuseStories(matrix.MisuseStories));
        failures.AddRange(EvaluateResidualRisk(matrix.ResidualRisk));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.ThreatModelPolicyVersion));

        if (liveThreatModelClaim)
            failures.Add("LiveThreatModelClaim forbidden until SOV-005a live independent TM review");

        return failures;
    }

    public static List<string> EvaluateBlockers(SovThreatMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("STR-001", "Satisfied"),
                     ("TEN-001", "Satisfied"),
                     ("INT-003", "Satisfied"),
                     ("SOV-004", "Satisfied"),
                     ("live-independent-tm-review", "Blocked"),
                     ("live-attack-tree-walkthrough", "Blocked"),
                     ("live-misuse-backlog-sync", "Blocked"),
                     ("live-residual-risk-signoff", "Blocked"),
                     ("SOV-006", "Satisfied"),
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

    public static List<string> EvaluateRequired(SovThreatRequiredFlag? section, string name)
    {
        if (section is null)
            return [$"{name} missing"];
        if (!section.Required)
            return [$"{name}.required must be true"];
        return [];
    }

    public static List<string> EvaluateMethodology(SovThreatMethodology? methodology)
    {
        var failures = new List<string>();
        if (methodology is null)
        {
            failures.Add("methodology missing");
            return failures;
        }

        if (!methodology.Required)
            failures.Add("methodology.required must be true");
        if (!methodology.StrideRequired)
            failures.Add("strideRequired must be true");
        return failures;
    }

    public static string? ValidateMethodology(bool stridePresent, SovThreatMethodology? methodology)
    {
        if (!stridePresent && (methodology?.StrideRequired ?? true))
            return "STRIDE methodology required";
        return null;
    }

    public static List<string> EvaluateTrustBoundaries(SovThreatTrustBoundaries? boundaries)
    {
        var failures = new List<string>();
        if (boundaries is null)
        {
            failures.Add("trustBoundaries missing");
            return failures;
        }

        if (!boundaries.Required)
            failures.Add("trustBoundaries.required must be true");
        if (!boundaries.InventoryRequired)
            failures.Add("inventoryRequired must be true");
        return failures;
    }

    public static string? ValidateTrustBoundaries(bool inventoryPresent, SovThreatTrustBoundaries? boundaries)
    {
        if (!inventoryPresent && (boundaries?.InventoryRequired ?? true))
            return "trust boundary inventory required";
        return null;
    }

    public static List<string> EvaluateCategories(SovThreatCategories? categories)
    {
        var failures = new List<string>();
        if (categories is null)
        {
            failures.Add("threatCategories missing");
            return failures;
        }

        if (!categories.Required)
            failures.Add("threatCategories.required must be true");
        foreach (var need in RequiredCategories)
        {
            if (!categories.Categories.Any(c => string.Equals(c, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"threatCategories missing {need}");
        }

        return failures;
    }

    public static string? ValidateCategoryCoverage(IEnumerable<string> missingCategories)
    {
        var missing = missingCategories.ToList();
        if (missing.Count > 0)
            return "threat categories missing: " + string.Join(", ", missing);
        return null;
    }

    public static List<string> EvaluateHighCoverage(SovThreatHighCoverage? coverage)
    {
        var failures = new List<string>();
        if (coverage is null)
        {
            failures.Add("highThreatCoverage missing");
            return failures;
        }

        if (!coverage.Required)
            failures.Add("highThreatCoverage.required must be true");
        if (!coverage.ControlRequired)
            failures.Add("controlRequired must be true");
        if (!coverage.TestRequired)
            failures.Add("testRequired must be true");
        if (!coverage.OwnerRequired)
            failures.Add("ownerRequired must be true");
        return failures;
    }

    public static string? ValidateHighThreatCoverage(
        bool controlPresent,
        bool testPresent,
        bool ownerPresent,
        SovThreatHighCoverage? coverage)
    {
        if (!controlPresent && (coverage?.ControlRequired ?? true))
            return "high threat control required";
        if (!testPresent && (coverage?.TestRequired ?? true))
            return "high threat test required";
        if (!ownerPresent && (coverage?.OwnerRequired ?? true))
            return "high threat owner required";
        return null;
    }

    public static List<string> EvaluateChangeTrigger(SovThreatChangeTrigger? trigger)
    {
        var failures = new List<string>();
        if (trigger is null)
        {
            failures.Add("changeTrigger missing");
            return failures;
        }

        if (!trigger.Required)
            failures.Add("changeTrigger.required must be true");
        if (!trigger.MaterialChangeUpdateRequired)
            failures.Add("materialChangeUpdateRequired must be true");
        return failures;
    }

    public static string? ValidateChangeTrigger(bool materialChangeCovered, SovThreatChangeTrigger? trigger)
    {
        if (!materialChangeCovered && (trigger?.MaterialChangeUpdateRequired ?? true))
            return "material change update required";
        return null;
    }

    public static List<string> EvaluateMisuseStories(SovThreatMisuseStories? misuse)
    {
        var failures = new List<string>();
        if (misuse is null)
        {
            failures.Add("misuseStories missing");
            return failures;
        }

        if (!misuse.Required)
            failures.Add("misuseStories.required must be true");
        if (!misuse.BacklogLinkageRequired)
            failures.Add("backlogLinkageRequired must be true");
        return failures;
    }

    public static string? ValidateMisuseLinkage(bool backlogLinked, SovThreatMisuseStories? misuse)
    {
        if (!backlogLinked && (misuse?.BacklogLinkageRequired ?? true))
            return "misuse backlog linkage required";
        return null;
    }

    public static List<string> EvaluateResidualRisk(SovThreatResidualRisk? residual)
    {
        var failures = new List<string>();
        if (residual is null)
        {
            failures.Add("residualRisk missing");
            return failures;
        }

        if (!residual.Required)
            failures.Add("residualRisk.required must be true");
        if (!residual.SignedRequired)
            failures.Add("signedRequired must be true");
        return failures;
    }

    public static string? ValidateResidualRisk(bool signed, SovThreatResidualRisk? residual)
    {
        if (!signed && (residual?.SignedRequired ?? true))
            return "residual risk signing required";
        return null;
    }

    public static List<string> EvaluateAudit(SovThreatAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, SovThreatAudit? audit)
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

    public static List<string> EvaluateMockSandbox(SovThreatMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveIndependentTmReviewForbidden)
            failures.Add("liveIndependentTmReviewForbidden must be true");
        if (!sandbox.LiveAttackTreeWalkthroughForbidden)
            failures.Add("liveAttackTreeWalkthroughForbidden must be true");
        if (!sandbox.LiveMisuseBacklogSyncForbidden)
            failures.Add("liveMisuseBacklogSyncForbidden must be true");
        if (!sandbox.LiveResidualRiskSignoffForbidden)
            failures.Add("liveResidualRiskSignoffForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(SovThreatSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(SovThreatSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.ThreatModelPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
