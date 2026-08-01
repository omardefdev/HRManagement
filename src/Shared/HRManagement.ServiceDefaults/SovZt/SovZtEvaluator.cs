namespace HRManagement.ServiceDefaults.SovZt;

public static class SovZtEvaluator
{
    private static readonly string[] RequiredAuditFields =
    [
        "principalId", "privilege", "reason", "approver", "actor", "occurredAt", "evidenceRef", "outcome",
        "sessionRef"
    ];

    private static readonly string[] RequiredTargets = ["database", "cloud", "k8s", "keycloak"];

    public static List<string> Evaluate(SovZtMatrixDocument matrix, bool livePamClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "SOV-004", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be SOV-004, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateNoStanding(matrix.NoStandingPrivilege));
        failures.AddRange(EvaluatePamJit(matrix.PamJit));
        failures.AddRange(EvaluateMfa(matrix.PhishingResistantMfa));
        failures.AddRange(EvaluateAdminWorkstations(matrix.AdminWorkstations));
        failures.AddRange(EvaluateRequired(matrix.SessionRecord, "sessionRecord"));
        failures.AddRange(EvaluateRequired(matrix.Approvals, "approvals"));
        failures.AddRange(EvaluateBreakGlass(matrix.BreakGlass));
        failures.AddRange(EvaluateRequired(matrix.ServiceAdminSeparation, "serviceAdminSeparation"));
        failures.AddRange(EvaluatePrivilegedTargets(matrix.PrivilegedTargets));
        failures.AddRange(EvaluateRecertification(matrix.AccessRecertification));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.ZeroTrustPolicyVersion));

        if (livePamClaim)
            failures.Add("LivePamClaim forbidden until SOV-004a live PAM workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(SovZtMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("IAM-003", "Satisfied"),
                     ("IAM-004", "Satisfied"),
                     ("SOV-003", "Satisfied"),
                     ("live-pam-workers", "Blocked"),
                     ("live-admin-workstation-enforcement", "Blocked"),
                     ("live-break-glass-soc", "Blocked"),
                     ("live-access-recertification", "Blocked"),
                     ("SOV-005", "Satisfied"),
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

    public static List<string> EvaluateRequired(SovZtRequiredFlag? section, string name)
    {
        if (section is null)
            return [$"{name} missing"];
        if (!section.Required)
            return [$"{name}.required must be true"];
        return [];
    }

    public static List<string> EvaluateNoStanding(SovZtNoStandingPrivilege? section)
    {
        var failures = new List<string>();
        if (section is null)
        {
            failures.Add("noStandingPrivilege missing");
            return failures;
        }

        if (!section.Required)
            failures.Add("noStandingPrivilege.required must be true");
        if (!section.SharedRootForbidden)
            failures.Add("sharedRootForbidden must be true");
        if (!section.PermanentAdminForbidden)
            failures.Add("permanentAdminForbidden must be true");
        return failures;
    }

    public static string? ValidateNoStandingPrivilege(bool hasStanding, bool sharedRoot, SovZtNoStandingPrivilege? section)
    {
        if (hasStanding && (section?.PermanentAdminForbidden ?? true))
            return "standing privilege forbidden";
        if (sharedRoot && (section?.SharedRootForbidden ?? true))
            return "shared root forbidden";
        return null;
    }

    public static List<string> EvaluatePamJit(SovZtPamJit? pam)
    {
        var failures = new List<string>();
        if (pam is null)
        {
            failures.Add("pamJit missing");
            return failures;
        }

        if (!pam.Required)
            failures.Add("pamJit.required must be true");
        if (!pam.TemporaryOnly)
            failures.Add("temporaryOnly must be true");
        if (!pam.ReasonRequired)
            failures.Add("reasonRequired must be true");
        if (!pam.ApprovalRequired)
            failures.Add("approvalRequired must be true");
        if (!pam.JeaRequired)
            failures.Add("jeaRequired must be true");
        return failures;
    }

    public static string? ValidateJit(bool temporary, bool reasonPresent, bool approved, SovZtPamJit? pam)
    {
        if (!temporary && (pam?.TemporaryOnly ?? true))
            return "JIT privilege must be temporary";
        if (!reasonPresent && (pam?.ReasonRequired ?? true))
            return "JIT reason required";
        if (!approved && (pam?.ApprovalRequired ?? true))
            return "JIT approval required";
        return null;
    }

    public static List<string> EvaluateMfa(SovZtPhishingResistantMfa? mfa)
    {
        var failures = new List<string>();
        if (mfa is null)
        {
            failures.Add("phishingResistantMfa missing");
            return failures;
        }

        if (!mfa.Required)
            failures.Add("phishingResistantMfa.required must be true");
        if (!mfa.AdminPathsRequired)
            failures.Add("adminPathsRequired must be true");
        return failures;
    }

    public static string? ValidateMfa(bool phishingResistant, SovZtPhishingResistantMfa? mfa)
    {
        if (!phishingResistant && (mfa?.Required ?? true))
            return "phishing-resistant MFA required";
        return null;
    }

    public static List<string> EvaluateAdminWorkstations(SovZtAdminWorkstations? ws)
    {
        var failures = new List<string>();
        if (ws is null)
        {
            failures.Add("adminWorkstations missing");
            return failures;
        }

        if (!ws.Required)
            failures.Add("adminWorkstations.required must be true");
        if (!ws.OrdinaryDeviceForbidden)
            failures.Add("ordinaryDeviceForbidden must be true");
        return failures;
    }

    public static string? ValidateAdminWorkstation(bool fromOrdinaryDevice, SovZtAdminWorkstations? ws)
    {
        if (fromOrdinaryDevice && (ws?.OrdinaryDeviceForbidden ?? true))
            return "admin from ordinary device forbidden";
        return null;
    }

    public static List<string> EvaluateBreakGlass(SovZtBreakGlass? bg)
    {
        var failures = new List<string>();
        if (bg is null)
        {
            failures.Add("breakGlass missing");
            return failures;
        }

        if (!bg.Required)
            failures.Add("breakGlass.required must be true");
        if (!bg.ImmediateAlertRequired)
            failures.Add("immediateAlertRequired must be true");
        if (!bg.PostUseReviewRequired)
            failures.Add("postUseReviewRequired must be true");
        return failures;
    }

    public static string? ValidateBreakGlass(bool alerted, bool reviewed, SovZtBreakGlass? bg)
    {
        if (!alerted && (bg?.ImmediateAlertRequired ?? true))
            return "break-glass immediate alert required";
        if (!reviewed && (bg?.PostUseReviewRequired ?? true))
            return "break-glass post-use review required";
        return null;
    }

    public static List<string> EvaluatePrivilegedTargets(SovZtPrivilegedTargets? targets)
    {
        var failures = new List<string>();
        if (targets is null)
        {
            failures.Add("privilegedTargets missing");
            return failures;
        }

        if (!targets.Required)
            failures.Add("privilegedTargets.required must be true");
        foreach (var need in RequiredTargets)
        {
            if (!targets.Targets.Any(t => string.Equals(t, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"privilegedTargets missing {need}");
        }

        return failures;
    }

    public static List<string> EvaluateRecertification(SovZtAccessRecertification? recert)
    {
        var failures = new List<string>();
        if (recert is null)
        {
            failures.Add("accessRecertification missing");
            return failures;
        }

        if (!recert.Required)
            failures.Add("accessRecertification.required must be true");
        if (!recert.QuarterlyRequired)
            failures.Add("quarterlyRequired must be true");
        if (!recert.DormantOrphanZeroRequired)
            failures.Add("dormantOrphanZeroRequired must be true");
        return failures;
    }

    public static string? ValidateRecertification(bool dormantOrphanPresent, SovZtAccessRecertification? recert)
    {
        if (dormantOrphanPresent && (recert?.DormantOrphanZeroRequired ?? true))
            return "dormant/orphan access forbidden";
        return null;
    }

    public static string? ValidateSessionRecord(bool recorded, SovZtRequiredFlag? session)
    {
        if (!recorded && (session?.Required ?? true))
            return "session record required";
        return null;
    }

    public static List<string> EvaluateAudit(SovZtAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, SovZtAudit? audit)
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

    public static List<string> EvaluateMockSandbox(SovZtMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LivePamForbidden)
            failures.Add("livePamForbidden must be true");
        if (!sandbox.LiveAdminWorkstationForbidden)
            failures.Add("liveAdminWorkstationForbidden must be true");
        if (!sandbox.LiveBreakGlassSocForbidden)
            failures.Add("liveBreakGlassSocForbidden must be true");
        if (!sandbox.LiveAccessRecertificationForbidden)
            failures.Add("liveAccessRecertificationForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(SovZtSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(SovZtSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.ZeroTrustPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
