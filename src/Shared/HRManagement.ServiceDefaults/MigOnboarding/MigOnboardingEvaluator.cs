namespace HRManagement.ServiceDefaults.MigOnboarding;

public static class MigOnboardingEvaluator
{
    private static readonly string[] RequiredGates =
    [
        "tenant", "config", "roles", "integrations", "data", "training", "support"
    ];

    private static readonly string[] RequiredOwners = ["data-owner", "security-owner", "ops-owner"];

    private static readonly string[] RequiredSignOffRoles =
    [
        "agency-data-owner", "security-owner", "ops-owner", "onboarding-office"
    ];

    private static readonly string[] RequiredExitModes = ["exit-incomplete", "rollback-to-pre-wave"];

    public static List<string> Evaluate(MigOnboardingMatrixDocument matrix, bool liveDualAgencyOnboardClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "MIG-007", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be MIG-007, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateReadinessGates(matrix));
        failures.AddRange(EvaluatePlaybook(matrix.Playbook));
        failures.AddRange(EvaluateSandbox(matrix.SandboxCertification));
        failures.AddRange(EvaluateWave(matrix.WaveCriteria));
        failures.AddRange(EvaluateOwners(matrix.RequiredOwners));
        failures.AddRange(EvaluateTelemetry(matrix.Telemetry));
        failures.AddRange(EvaluateExitRollback(matrix.ExitRollback));
        failures.AddRange(EvaluateAgencies(matrix.Agencies));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.OnboardingPolicyVersion));

        if (liveDualAgencyOnboardClaim)
            failures.Add("LiveDualAgencyOnboardClaim forbidden until MIG-007a-d onboarding workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(MigOnboardingMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("MIG-006", "Satisfied"),
                     ("STR-004", "Satisfied"),
                     ("EXT-006", "NotStarted"),
                     ("PROD-002", "Satisfied"),
                     ("ONB-001", "Satisfied"),
                     ("agency-extract-access", "Blocked"),
                     ("live-dual-agency-onboard", "Blocked")
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

    public static List<string> EvaluateReadinessGates(MigOnboardingMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.ReadinessGates.Count < 7)
            failures.Add($"need >= 7 readinessGates (got {matrix.ReadinessGates.Count})");

        foreach (var need in RequiredGates)
        {
            var g = matrix.ReadinessGates.FirstOrDefault(x =>
                string.Equals(x.Id, need, StringComparison.OrdinalIgnoreCase));
            if (g is null)
                failures.Add($"missing readiness gate {need}");
            else if (!g.Mandatory)
                failures.Add($"gate {need} must be mandatory");
        }

        foreach (var g in matrix.ReadinessGates.Where(x => !x.Mandatory))
            failures.Add($"gate {g.Id} must be mandatory");

        return failures;
    }

    public static List<string> EvaluatePlaybook(MigOnboardingPlaybook? playbook)
    {
        var failures = new List<string>();
        if (playbook is null)
        {
            failures.Add("playbook missing");
            return failures;
        }

        if (!playbook.NoforkRequired)
            failures.Add("noforkRequired must be true");
        if (playbook.StandardSteps.Count < 5)
            failures.Add("playbook steps too few");
        return failures;
    }

    public static string? ValidateNofork(bool proposedFork, MigOnboardingPlaybook? playbook)
    {
        if ((playbook?.NoforkRequired ?? true) && proposedFork)
            return "per-agency fork forbidden";
        return null;
    }

    public static List<string> EvaluateSandbox(MigSandboxCertification? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("sandboxCertification missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("sandboxCertification.labStub must be true");
        if (!sandbox.LivePortalForbidden)
            failures.Add("livePortalForbidden must be true");
        return failures;
    }

    public static string? ValidateLivePortalClaim(bool livePortal, MigSandboxCertification? sandbox)
    {
        if (livePortal && (sandbox?.LivePortalForbidden ?? true))
            return "live portal forbidden in this binding";
        return null;
    }

    public static List<string> EvaluateWave(MigWaveCriteria? wave)
    {
        var failures = new List<string>();
        if (wave is null)
        {
            failures.Add("waveCriteria missing");
            return failures;
        }

        if (!wave.SignOffsRequiredBeforeWave)
            failures.Add("signOffsRequiredBeforeWave must be true");
        if (wave.RequiredSignOffRoles.Count < 3)
            failures.Add("wave sign-off roles incomplete");
        foreach (var need in RequiredSignOffRoles)
        {
            if (!wave.RequiredSignOffRoles.Any(r => string.Equals(r, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"wave missing sign-off role {need}");
        }

        return failures;
    }

    public static string? ValidateWaveSignOffs(
        bool waveStarted,
        bool signOffsComplete,
        MigWaveCriteria? wave)
    {
        if ((wave?.SignOffsRequiredBeforeWave ?? true) && waveStarted && !signOffsComplete)
            return "wave without complete sign-offs forbidden";
        return null;
    }

    public static List<string> EvaluateOwners(IReadOnlyList<string> owners)
    {
        var failures = new List<string>();
        foreach (var need in RequiredOwners)
        {
            if (!owners.Any(o => string.Equals(o, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing required owner {need}");
        }

        return failures;
    }

    public static string? ValidateOwners(IEnumerable<string> presentOwners, IReadOnlyList<string>? required)
    {
        var present = presentOwners.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in required ?? RequiredOwners)
        {
            if (!present.Contains(need))
                return $"missing required owner {need}";
        }

        return null;
    }

    public static List<string> EvaluateTelemetry(MigOnboardingTelemetry? telemetry)
    {
        var failures = new List<string>();
        if (telemetry is null)
        {
            failures.Add("telemetry missing");
            return failures;
        }

        if (telemetry.LeadTimeTargetDays <= 0)
            failures.Add("leadTimeTargetDays must be > 0");
        if (telemetry.DefectBudget < 0)
            failures.Add("defectBudget must be >= 0");
        return failures;
    }

    public static string? ValidateLeadTime(int days, int target)
    {
        if (days > target)
            return "lead time exceeds target";
        return null;
    }

    public static string? ValidateDefects(int count, int budget)
    {
        if (count > budget)
            return "defects exceed budget";
        return null;
    }

    public static List<string> EvaluateExitRollback(MigExitRollbackPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("exitRollback missing");
            return failures;
        }

        if (!policy.Required)
            failures.Add("exitRollback.required must be true");
        foreach (var need in RequiredExitModes)
        {
            if (!policy.Modes.Any(m => string.Equals(m, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"exitRollback missing mode {need}");
        }

        return failures;
    }

    public static List<string> EvaluateAgencies(IReadOnlyList<MigOnboardingAgency> agencies)
    {
        var failures = new List<string>();
        if (agencies.Count < 2)
            failures.Add("need >= 2 lab agencies");
        else if (string.Equals(agencies[0].Id, agencies[1].Id, StringComparison.OrdinalIgnoreCase))
            failures.Add("agencies must be distinct");
        return failures;
    }

    public static List<string> EvaluateSigning(MigOnboardingSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(MigOnboardingSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.OnboardingPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
