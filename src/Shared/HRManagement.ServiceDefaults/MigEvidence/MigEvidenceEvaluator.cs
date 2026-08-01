namespace HRManagement.ServiceDefaults.MigEvidence;

public static class MigEvidenceEvaluator
{
    private static readonly string[] RequiredBuckets = ["Migrated", "Rejected", "Deferred"];
    private static readonly string[] RequiredDimensions =
        ["counts", "sums", "hashes", "payrollControlTotals", "rejects", "lineage"];
    private static readonly string[] RequiredArtifacts =
    [
        "source-counts", "bucket-breakdown", "payroll-control", "lineage-export",
        "reject-register", "checksum-manifest", "sample-results", "dual-control-signoff"
    ];
    private static readonly string[] DualRoles = ["migration-qa", "internal-audit"];
    private static readonly string[] WaveRoles = ["agency-data-owner", "migration-qa", "internal-audit"];

    public static List<string> Evaluate(MigEvidenceMatrixDocument matrix, bool liveAgencyReconClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "MIG-005", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be MIG-005, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateBuckets(matrix));
        failures.AddRange(EvaluateDimensions(matrix));
        failures.AddRange(EvaluateFinancial(matrix.FinancialPolicy));
        failures.AddRange(EvaluateDualControl(matrix.DualControl));
        failures.AddRange(EvaluateEvidencePackage(matrix.EvidencePackage));
        failures.AddRange(EvaluateSampling(matrix.Sampling));
        failures.AddRange(EvaluateWaveSignOff(matrix.AgencyWaveSignOff));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.EvidencePolicyVersion));

        if (liveAgencyReconClaim)
            failures.Add("LiveAgencyReconClaim forbidden until MIG-005a-d recon workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(MigEvidenceMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("MIG-003", "Satisfied"),
                     ("OBS-003", "Satisfied"),
                     ("agency-extract-access", "Blocked"),
                     ("live-agency-recon", "Blocked"),
                     ("live-independent-sql", "Blocked")
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

    public static List<string> EvaluateBuckets(MigEvidenceMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.AccountedBuckets.Count < 3)
            failures.Add($"need >= 3 accountedBuckets (got {matrix.AccountedBuckets.Count})");

        foreach (var need in RequiredBuckets)
        {
            var b = matrix.AccountedBuckets.FirstOrDefault(x =>
                string.Equals(x.Id, need, StringComparison.OrdinalIgnoreCase));
            if (b is null)
            {
                failures.Add($"missing accounted bucket {need}");
                continue;
            }

            if ((string.Equals(need, "Rejected", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(need, "Deferred", StringComparison.OrdinalIgnoreCase))
                && !b.ReasonRequired)
            {
                failures.Add($"bucket {need}: reasonRequired must be true");
            }
        }

        return failures;
    }

    public static List<string> EvaluateDimensions(MigEvidenceMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.ReconDimensions.Count < 6)
            failures.Add($"need >= 6 reconDimensions (got {matrix.ReconDimensions.Count})");

        foreach (var need in RequiredDimensions)
        {
            if (!matrix.ReconDimensions.Any(d => string.Equals(d, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing reconDimension {need}");
        }

        return failures;
    }

    public static List<string> EvaluateFinancial(MigFinancialPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("financialPolicy missing");
            return failures;
        }

        if (!policy.UnexplainedVarianceForbidden)
            failures.Add("unexplainedVarianceForbidden must be true");
        if (Math.Abs(policy.ControlTotalTolerance) > double.Epsilon)
            failures.Add("controlTotalTolerance must be 0");
        if (!policy.PayrollControlRequired)
            failures.Add("payrollControlRequired must be true");
        return failures;
    }

    public static List<string> EvaluateDualControl(MigDualControlPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("dualControl missing");
            return failures;
        }

        if (policy.MinSigners < 2)
            failures.Add("dualControl minSigners must be >= 2");
        if (!policy.RequireDistinctRoles)
            failures.Add("requireDistinctRoles must be true");
        foreach (var need in DualRoles)
        {
            if (!policy.Roles.Any(r => string.Equals(r, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"dualControl missing role {need}");
        }

        return failures;
    }

    public static string? ValidateDualControlSigners(
        IReadOnlyList<(string Role, string Actor)> signers,
        MigDualControlPolicy? policy)
    {
        var min = policy?.MinSigners ?? 2;
        if (signers.Count < min)
            return $"dual control needs >= {min} signers";
        if (policy?.RequireDistinctRoles == true)
        {
            var roles = signers.Select(s => s.Role).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (roles < 2)
                return "dual control requires distinct roles";
        }

        return null;
    }

    public static List<string> EvaluateEvidencePackage(MigEvidencePackagePolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("evidencePackage missing");
            return failures;
        }

        if (!policy.Immutable)
            failures.Add("evidencePackage.immutable must be true");
        if (!string.Equals(policy.ContentHashAlg, "sha256", StringComparison.OrdinalIgnoreCase))
            failures.Add("contentHashAlg must be sha256");
        if (policy.RequiredArtifacts.Count < 8)
            failures.Add($"need >= 8 requiredArtifacts (got {policy.RequiredArtifacts.Count})");
        foreach (var need in RequiredArtifacts)
        {
            if (!policy.RequiredArtifacts.Any(a => string.Equals(a, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"evidencePackage missing artifact {need}");
        }

        return failures;
    }

    public static string? ValidateEvidenceImmutability(bool claimedImmutable, bool hashMatch)
    {
        if (!claimedImmutable)
            return "evidence package must be immutable";
        if (!hashMatch)
            return "evidence content hash mismatch";
        return null;
    }

    public static List<string> EvaluateSampling(MigSamplingPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("sampling missing");
            return failures;
        }

        if (!policy.BusinessSignOffRequired)
            failures.Add("businessSignOffRequired must be true");
        if (policy.StatisticalMin < 1)
            failures.Add("statisticalMin must be >= 1");
        return failures;
    }

    public static List<string> EvaluateWaveSignOff(MigAgencyWaveSignOffPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("agencyWaveSignOff missing");
            return failures;
        }

        if (!policy.PerWaveRequired)
            failures.Add("perWaveRequired must be true");
        foreach (var need in WaveRoles)
        {
            if (!policy.Roles.Any(r => string.Equals(r, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"agencyWaveSignOff missing role {need}");
        }

        return failures;
    }

    public static List<string> EvaluateSigning(MigEvidenceSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(MigEvidenceSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.EvidencePolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }

    public static string? ValidateAccountedBreakdown(
        int migrated,
        int rejected,
        int deferred,
        int sourceTotal,
        bool rejectedReasonsPresent,
        bool deferredReasonsPresent,
        MigEvidenceMatrixDocument matrix)
    {
        if (migrated + rejected + deferred != sourceTotal)
            return "accounted buckets do not sum to sourceTotal";
        var rejectedBucket = matrix.AccountedBuckets.FirstOrDefault(b =>
            string.Equals(b.Id, "Rejected", StringComparison.OrdinalIgnoreCase));
        var deferredBucket = matrix.AccountedBuckets.FirstOrDefault(b =>
            string.Equals(b.Id, "Deferred", StringComparison.OrdinalIgnoreCase));
        if (rejected > 0 && rejectedBucket?.ReasonRequired == true && !rejectedReasonsPresent)
            return "rejected reasons required";
        if (deferred > 0 && deferredBucket?.ReasonRequired == true && !deferredReasonsPresent)
            return "deferred reasons required";
        return null;
    }

    public static string? ValidateFinancialVariance(double unexplainedAmount, MigFinancialPolicy? policy)
    {
        var tolerance = policy?.ControlTotalTolerance ?? 0;
        if ((policy?.UnexplainedVarianceForbidden ?? true) && Math.Abs(unexplainedAmount) > tolerance)
            return "unexplained financial variance forbidden";
        return null;
    }

    public static string? ValidateIndependentSqlClaim(bool liveIndependentSql, MigEvidenceMatrixDocument matrix)
    {
        var blocked = matrix.Blockers.Any(b =>
            string.Equals(b.Id, "live-independent-sql", StringComparison.OrdinalIgnoreCase)
            && string.Equals(b.Status, "Blocked", StringComparison.OrdinalIgnoreCase));
        if (liveIndependentSql && blocked)
            return "liveIndependentSql forbidden while live-independent-sql is Blocked";
        return null;
    }
}
