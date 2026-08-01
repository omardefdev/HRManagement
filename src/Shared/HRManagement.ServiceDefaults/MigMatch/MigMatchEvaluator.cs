namespace HRManagement.ServiceDefaults.MigMatch;

public static class MigMatchEvaluator
{
    private static readonly string[] RequiredStrategies =
    [
        "DeterministicNationalId", "DeterministicComposite", "ProbabilisticNameDob", "ExceptionQueue"
    ];

    private static readonly string[] RequiredMergeAuditFields =
    [
        "mergedAt", "actor", "evidenceRef", "survivorId", "sourceId", "reversible"
    ];

    public static List<string> Evaluate(MigMatchMatrixDocument matrix, bool liveIdentityMatchClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "MIG-004", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be MIG-004, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateStrategies(matrix));
        failures.AddRange(EvaluateThresholds(matrix.Thresholds));
        failures.AddRange(EvaluateNationalIdExceptions(matrix));
        failures.AddRange(EvaluateMergeAudit(matrix.MergeAudit));
        failures.AddRange(EvaluateReviewPrivacy(matrix.ReviewPrivacy));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.MatchPolicyVersion));
        failures.AddRange(EvaluateFraud(matrix));
        failures.AddRange(EvaluateFalsePositiveControls(matrix.FalsePositiveControls));

        if (liveIdentityMatchClaim)
            failures.Add("LiveIdentityMatchClaim forbidden until MIG-004a-d matcher workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(MigMatchMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("MIG-002", "Satisfied"),
                     ("DATA-002", "Satisfied"),
                     ("GOV-001", "Satisfied"),
                     ("GOV-003-merge", "Satisfied"),
                     ("agency-extract-access", "Blocked"),
                     ("L10N-002", "Done"),
                     ("live-labelled-metrics", "Blocked")
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

    public static List<string> EvaluateStrategies(MigMatchMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.MatchStrategies.Count < 4)
            failures.Add($"need >= 4 matchStrategies (got {matrix.MatchStrategies.Count})");

        var ids = matrix.MatchStrategies.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in RequiredStrategies)
        {
            if (!ids.Contains(need))
                failures.Add($"missing strategy {need}");
        }

        return failures;
    }

    public static List<string> EvaluateThresholds(MigMatchThresholds? t)
    {
        var failures = new List<string>();
        if (t is null)
        {
            failures.Add("thresholds missing");
            return failures;
        }

        if (!t.HighRiskAutoMergeForbidden)
            failures.Add("highRiskAutoMergeForbidden must be true");
        if (!string.Equals(t.AutoMergeMaxRisk, "Low", StringComparison.OrdinalIgnoreCase))
            failures.Add("autoMergeMaxRisk must be Low");
        if (t.PrecisionTarget < 0.9)
            failures.Add("precisionTarget must be >= 0.9");
        if (t.RecallTarget < 0.85)
            failures.Add("recallTarget must be >= 0.85");
        return failures;
    }

    public static List<string> EvaluateNationalIdExceptions(MigMatchMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.NationalIdExceptions.Count < 1)
        {
            failures.Add("nationalIdExceptions empty");
            return failures;
        }

        foreach (var ex in matrix.NationalIdExceptions)
        {
            if (!ex.AutoMergeForbidden)
                failures.Add($"nationalIdException {ex.Code}: autoMergeForbidden must be true");
        }

        return failures;
    }

    public static List<string> EvaluateMergeAudit(MigMergeAuditPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("mergeAudit missing");
            return failures;
        }

        if (!policy.ReversibleRequired)
            failures.Add("reversibleRequired must be true");
        foreach (var need in RequiredMergeAuditFields)
        {
            if (!policy.RequiredFields.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"mergeAudit missing field {need}");
        }

        return failures;
    }

    public static string? ValidateMergeAuditFields(IReadOnlyDictionary<string, string?> fields, bool reversible, MigMergeAuditPolicy? policy)
    {
        if (policy is not null && policy.ReversibleRequired && !reversible)
            return "merge must be reversible";
        foreach (var need in RequiredMergeAuditFields)
        {
            if (string.Equals(need, "reversible", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!fields.TryGetValue(need, out var v) || string.IsNullOrWhiteSpace(v))
                return $"merge audit missing field {need}";
        }

        return null;
    }

    public static List<string> EvaluateReviewPrivacy(MigReviewPrivacyPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("reviewPrivacy missing");
            return failures;
        }

        if (!policy.TenantScopedReview)
            failures.Add("tenantScopedReview must be true");
        if (!policy.ForbidCrossAgencyPii)
            failures.Add("forbidCrossAgencyPii must be true");
        if (string.IsNullOrWhiteSpace(policy.MaskingMode))
            failures.Add("maskingMode missing");
        return failures;
    }

    public static string? ValidateReviewPrivacy(bool crossTenant, MigReviewPrivacyPolicy? policy)
    {
        if (policy is null)
            return "reviewPrivacy missing";
        if (crossTenant && policy.ForbidCrossAgencyPii)
            return "cross-agency PII review forbidden";
        return null;
    }

    public static List<string> EvaluateSigning(MigMatchSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(MigMatchSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.MatchPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }

    public static List<string> EvaluateFraud(MigMatchMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.FraudIndicators.Count < 3)
            failures.Add($"need >= 3 fraudIndicators (got {matrix.FraudIndicators.Count})");
        foreach (var f in matrix.FraudIndicators)
        {
            if (!f.ForcesReview)
                failures.Add($"fraudIndicator {f.Id}: forcesReview must be true");
        }

        return failures;
    }

    public static List<string> EvaluateFalsePositiveControls(MigFalsePositiveControls? c)
    {
        var failures = new List<string>();
        if (c is null)
        {
            failures.Add("falsePositiveControls missing");
            return failures;
        }

        if (!c.RequireEvidenceForAutoMerge)
            failures.Add("requireEvidenceForAutoMerge must be true");
        return failures;
    }

    public static string? ValidateProposedOutcome(
        string? outcome,
        string? risk,
        string? evidenceRef,
        MigMatchThresholds? thresholds,
        MigFalsePositiveControls? fp)
    {
        var isAuto = string.Equals(outcome, "AutoMerge", StringComparison.OrdinalIgnoreCase);
        if (!isAuto)
            return null;

        if (string.Equals(risk, "High", StringComparison.OrdinalIgnoreCase)
            && (thresholds?.HighRiskAutoMergeForbidden ?? true))
        {
            return "high-risk AutoMerge forbidden";
        }

        if ((fp?.RequireEvidenceForAutoMerge ?? true) && string.IsNullOrWhiteSpace(evidenceRef))
            return "AutoMerge requires evidenceRef";

        return null;
    }

    public static string? ValidateLiveMetricsClaim(bool liveAgencyEval, MigMatchMatrixDocument matrix)
    {
        var blocked = matrix.Blockers.Any(b =>
            string.Equals(b.Id, "live-labelled-metrics", StringComparison.OrdinalIgnoreCase)
            && string.Equals(b.Status, "Blocked", StringComparison.OrdinalIgnoreCase));
        if (liveAgencyEval && blocked)
            return "liveAgencyEval forbidden while live-labelled-metrics is Blocked";
        return null;
    }
}
