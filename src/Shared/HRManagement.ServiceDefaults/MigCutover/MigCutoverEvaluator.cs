namespace HRManagement.ServiceDefaults.MigCutover;

public static class MigCutoverEvaluator
{
    private static readonly string[] RequiredGoNoGoCriteria =
    [
        "rehearsalsComplete", "evidencePackageOk", "rollbackExercised", "deltaNoLoss", "legacySecuredPlan"
    ];

    private static readonly string[] RequiredCheckpoints = ["T+0", "T+1", "T+30"];

    public static List<string> Evaluate(MigCutoverMatrixDocument matrix, bool liveAgencyCutoverClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "MIG-006", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be MIG-006, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateRehearsals(matrix));
        failures.AddRange(EvaluateChangeFreeze(matrix.ChangeFreeze));
        failures.AddRange(EvaluateDelta(matrix.DeltaCapture));
        failures.AddRange(EvaluateParallelAndLock(matrix));
        failures.AddRange(EvaluateDns(matrix.DnsCutover));
        failures.AddRange(EvaluateRollback(matrix.Rollback));
        failures.AddRange(EvaluateGoNoGo(matrix.GoNoGo));
        failures.AddRange(EvaluateLegacy(matrix.LegacyAccess));
        failures.AddRange(EvaluatePostCutover(matrix.PostCutoverRecon));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.CutoverPolicyVersion));

        if (liveAgencyCutoverClaim)
            failures.Add("LiveAgencyCutoverClaim forbidden until MIG-006a-d cutover workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(MigCutoverMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("MIG-005", "Satisfied"),
                     ("OPS-003", "Satisfied"),
                     ("REL-001", "Satisfied"),
                     ("agency-extract-access", "Blocked"),
                     ("live-cutover-perf", "Blocked"),
                     ("live-agency-recon", "Blocked"),
                     ("live-rehearsal-at-scale", "Blocked")
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

    public static List<string> EvaluateRehearsals(MigCutoverMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.MinRehearsals < 3)
            failures.Add("minRehearsals must be >= 3");
        if (matrix.Rehearsals.Count < 3)
            failures.Add($"need >= 3 rehearsals (got {matrix.Rehearsals.Count})");

        foreach (var r in matrix.Rehearsals)
        {
            var err = ValidateRehearsal(r);
            if (err is not null)
                failures.Add(err);
        }

        return failures;
    }

    public static string? ValidateRehearsal(MigCutoverRehearsal rehearsal)
    {
        if (string.IsNullOrWhiteSpace(rehearsal.Id))
            return "rehearsal missing id";
        if (!rehearsal.Timed)
            return $"rehearsal {rehearsal.Id}: timed must be true";
        if (!rehearsal.WithinWindow)
            return $"rehearsal {rehearsal.Id}: withinWindow must be true";
        if (!rehearsal.VolumeOk)
            return $"rehearsal {rehearsal.Id}: volumeOk must be true";
        return null;
    }

    public static List<string> EvaluateChangeFreeze(MigChangeFreezePolicy? policy)
    {
        if (policy is null)
            return ["changeFreeze missing"];
        if (!policy.Required)
            return ["changeFreeze.required must be true"];
        return [];
    }

    public static List<string> EvaluateDelta(MigDeltaCapturePolicy? policy)
    {
        if (policy is null)
            return ["deltaCapture missing"];
        if (!policy.NoLossRequired)
            return ["noLossRequired must be true"];
        return [];
    }

    public static string? ValidateDelta(int lostCount, MigDeltaCapturePolicy? policy)
    {
        if ((policy?.NoLossRequired ?? true) && lostCount > 0)
            return "delta loss forbidden";
        return null;
    }

    public static List<string> EvaluateParallelAndLock(MigCutoverMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.ParallelMode is null)
            failures.Add("parallelMode missing");
        else
        {
            if (!matrix.ParallelMode.Read)
                failures.Add("parallelMode.read must be true");
            if (!matrix.ParallelMode.Run)
                failures.Add("parallelMode.run must be true");
        }

        if (matrix.UserLock is null)
            failures.Add("userLock missing");
        else if (!matrix.UserLock.RequiredBeforeCutover)
            failures.Add("userLock.requiredBeforeCutover must be true");

        return failures;
    }

    public static List<string> EvaluateDns(MigDnsCutoverPolicy? policy)
    {
        if (policy is null)
            return ["dnsCutover missing"];
        if (!policy.LiveForbiddenInThisPack)
            return ["dnsCutover.liveForbiddenInThisPack must be true"];
        return [];
    }

    public static string? ValidateLiveDnsClaim(bool liveDns, MigDnsCutoverPolicy? policy)
    {
        if (liveDns && (policy?.LiveForbiddenInThisPack ?? true))
            return "live DNS cutover forbidden in this binding";
        return null;
    }

    public static List<string> EvaluateRollback(MigRollbackPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("rollback missing");
            return failures;
        }

        if (!policy.MustBeExercised)
            failures.Add("rollback.mustBeExercised must be true");
        if (!policy.PaperOnlyForbidden)
            failures.Add("paperOnlyForbidden must be true");
        if (policy.WindowHours <= 0)
            failures.Add("rollback.windowHours must be > 0");
        return failures;
    }

    public static string? ValidateRollback(bool exercised, bool paperOnly, MigRollbackPolicy? policy)
    {
        if ((policy?.MustBeExercised ?? true) && !exercised)
            return "rollback must be exercised";
        if ((policy?.PaperOnlyForbidden ?? true) && paperOnly)
            return "paper-only rollback forbidden";
        return null;
    }

    public static List<string> EvaluateGoNoGo(MigGoNoGoPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("goNoGo missing");
            return failures;
        }

        if (!policy.DecisionRequired)
            failures.Add("goNoGo.decisionRequired must be true");
        if (policy.Criteria.Count < 5)
            failures.Add("goNoGo needs >= 5 criteria");
        foreach (var need in RequiredGoNoGoCriteria)
        {
            if (!policy.Criteria.Any(c => string.Equals(c, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"goNoGo missing criterion {need}");
        }

        return failures;
    }

    public static string? ValidateGoNoGo(
        IReadOnlyDictionary<string, bool> criteriaMet,
        MigGoNoGoPolicy? policy)
    {
        foreach (var need in policy?.Criteria ?? RequiredGoNoGoCriteria.ToList())
        {
            if (!criteriaMet.TryGetValue(need, out var ok) || !ok)
                return $"goNoGo criterion {need} not met";
        }

        return null;
    }

    public static List<string> EvaluateLegacy(MigLegacyAccessPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("legacyAccess missing");
            return failures;
        }

        if (!policy.ReadOnlyAfterCutover)
            failures.Add("readOnlyAfterCutover must be true");
        if (!policy.SecuredRequired)
            failures.Add("securedRequired must be true");
        return failures;
    }

    public static string? ValidateLegacyAccess(bool readOnly, bool secured, MigLegacyAccessPolicy? policy)
    {
        if ((policy?.ReadOnlyAfterCutover ?? true) && !readOnly)
            return "legacy must be read-only after cutover";
        if ((policy?.SecuredRequired ?? true) && !secured)
            return "legacy access must be secured";
        return null;
    }

    public static List<string> EvaluatePostCutover(MigPostCutoverReconPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("postCutoverRecon missing");
            return failures;
        }

        foreach (var need in RequiredCheckpoints)
        {
            if (!policy.Checkpoints.Any(c => string.Equals(c, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"postCutoverRecon missing checkpoint {need}");
        }

        return failures;
    }

    public static List<string> EvaluateSigning(MigCutoverSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(MigCutoverSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.CutoverPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
