namespace HRManagement.ServiceDefaults.ProdEss;

public static class ProdEssEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["journeyId", "actorRole", "agencyId", "outcome", "actor", "occurredAt", "evidenceRef"];

    private static readonly string[] RequiredHighVolumeJourneys =
    [
        "profile-correction", "leave-request", "payslip-tax", "documents",
        "manager-approvals", "delegation", "inbox"
    ];

    public static List<string> Evaluate(ProdEssMatrixDocument matrix, bool liveEssClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "PROD-003", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be PROD-003, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateHighVolume(matrix.HighVolumeJourneys));
        failures.AddRange(EvaluateCorrection(matrix.CorrectionVisibility));
        failures.AddRange(EvaluateManagerScope(matrix.ManagerScope));
        failures.AddRange(EvaluateUsage(matrix.UsageTelemetry));
        failures.AddRange(EvaluatePrivacy(matrix.Privacy));
        failures.AddRange(EvaluateDeviceSession(matrix.DeviceSession));
        failures.AddRange(EvaluatePersonaUat(matrix.PersonaUat));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.EmployeeSelfServicePolicyVersion));

        if (liveEssClaim)
            failures.Add("LiveEmployeeSelfServiceClaim forbidden until PROD-003a-e employee selfservice workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ProdEssMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("IAM-003", "Satisfied"),
                     ("FUN-001", "Satisfied"),
                     ("FUN-002", "Satisfied"),
                     ("FUN-003", "Satisfied"),
                     ("FUN-004", "Satisfied"),
                     ("FUN-005", "Satisfied"),
                     ("FUN-006", "Satisfied"),
                     ("UX-002", "Satisfied"),
                     ("IAM-002", "NotStarted"),
                     ("live-employee-portal", "Blocked"),
                     ("live-manager-portal", "Blocked"),
                     ("live-usage-telemetry", "Blocked"),
                     ("ESS-001", "NotStarted"),
                     ("MSS-001", "NotStarted"),
                     ("PROD-004", "Satisfied")
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

    public static List<string> EvaluateHighVolume(ProdEssHighVolumeJourneys? hv)
    {
        var failures = new List<string>();
        if (hv is null)
        {
            failures.Add("highVolumeJourneys missing");
            return failures;
        }

        if (!hv.Required)
            failures.Add("highVolumeJourneys.required must be true");
        if (!hv.ExceptionOnlyBackOffice)
            failures.Add("exceptionOnlyBackOffice must be true");
        foreach (var need in RequiredHighVolumeJourneys)
        {
            if (!hv.Journeys.Any(j => string.Equals(j, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing high-volume journey {need}");
        }

        return failures;
    }

    public static string? ValidateHighVolumeJourney(
        bool completedWithoutBackOffice,
        bool isException,
        ProdEssHighVolumeJourneys? hv)
    {
        if (hv?.ExceptionOnlyBackOffice != true)
            return null;
        if (!completedWithoutBackOffice && !isException)
            return "high-volume journey requires back-office only for exception";
        return null;
    }

    public static List<string> EvaluateCorrection(ProdEssCorrectionVisibility? cv)
    {
        var failures = new List<string>();
        if (cv is null)
        {
            failures.Add("correctionVisibility missing");
            return failures;
        }

        if (!cv.SourceVisible)
            failures.Add("correctionVisibility.sourceVisible must be true");
        if (!cv.StatusVisible)
            failures.Add("correctionVisibility.statusVisible must be true");
        return failures;
    }

    public static string? ValidateCorrectionVisibility(
        bool sourceVisible,
        bool statusVisible,
        ProdEssCorrectionVisibility? cv)
    {
        if (!(cv?.SourceVisible ?? true) || !(cv?.StatusVisible ?? true))
            return "correction visibility policy incomplete";
        if (!sourceVisible || !statusVisible)
            return "correction source/status must be visible";
        return null;
    }

    public static List<string> EvaluateManagerScope(ProdEssManagerScope? scope)
    {
        var failures = new List<string>();
        if (scope is null)
        {
            failures.Add("managerScope missing");
            return failures;
        }

        if (!scope.Enforced)
            failures.Add("managerScope.enforced must be true");
        if (!scope.OutsideScopeForbidden)
            failures.Add("outsideScopeForbidden must be true");
        return failures;
    }

    public static string? ValidateManagerScope(bool withinScope, ProdEssManagerScope? scope)
    {
        if (!withinScope && (scope?.OutsideScopeForbidden ?? true))
            return "manager outside scope forbidden";
        return null;
    }

    public static List<string> EvaluateUsage(ProdEssUsageTelemetry? usage)
    {
        var failures = new List<string>();
        if (usage is null)
        {
            failures.Add("usageTelemetry missing");
            return failures;
        }

        if (!usage.Required)
            failures.Add("usageTelemetry.required must be true");
        if (!usage.LabStubAllowed)
            failures.Add("labStubAllowed must be true");
        if (!usage.LiveForbidden)
            failures.Add("usageTelemetry.liveForbidden must be true");
        return failures;
    }

    public static string? ValidateUsageMeasured(bool telemetryPresent, ProdEssUsageTelemetry? usage)
    {
        if (!telemetryPresent && (usage?.Required ?? true))
            return "usage telemetry required";
        return null;
    }

    public static List<string> EvaluatePrivacy(ProdEssPrivacy? privacy)
    {
        if (privacy is null)
            return ["privacy missing"];
        if (!privacy.MinimizationRequired)
            return ["minimizationRequired must be true"];
        return [];
    }

    public static List<string> EvaluateDeviceSession(ProdEssDeviceSession? device)
    {
        var failures = new List<string>();
        if (device is null)
        {
            failures.Add("deviceSession missing");
            return failures;
        }

        if (!device.LostSessionHandled)
            failures.Add("lostSessionHandled must be true");
        if (!device.DeviceSecurityRequired)
            failures.Add("deviceSecurityRequired must be true");
        return failures;
    }

    public static string? ValidateLostSessionHandled(
        bool sessionLost,
        bool recoveredSafely,
        ProdEssDeviceSession? device)
    {
        if (sessionLost && !recoveredSafely && (device?.LostSessionHandled ?? true))
            return "lost session must be recovered safely";
        return null;
    }

    public static List<string> EvaluatePersonaUat(ProdEssPersonaUat? persona)
    {
        var failures = new List<string>();
        if (persona is null)
        {
            failures.Add("personaUat missing");
            return failures;
        }

        if (!persona.MultiAgencyRequired)
            failures.Add("multiAgencyRequired must be true");
        if (!persona.MobileDesktopRequired)
            failures.Add("mobileDesktopRequired must be true");
        return failures;
    }

    public static List<string> EvaluateAudit(ProdEssAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, ProdEssAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ProdEssMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveEmployeePortalForbidden)
            failures.Add("liveEmployeePortalForbidden must be true");
        if (!sandbox.LiveManagerPortalForbidden)
            failures.Add("liveManagerPortalForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ProdEssSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ProdEssSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.EmployeeSelfServicePolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
