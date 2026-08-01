namespace HRManagement.ServiceDefaults.SovBaseline;

public static class SovBaselineEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["controlId", "owner", "evidenceRef", "status", "outcome", "actor", "occurredAt", "assessmentRef"];

    private static readonly string[] RequiredCrosswalk =
        ["NIST-CSF", "NIST-800-53B", "ISO-27001", "ASVS-5", "SSDF"];

    private static readonly string[] RequiredStatuses =
        ["applicable", "implemented", "partial", "not-applicable"];

    public static List<string> Evaluate(SovBaselineMatrixDocument matrix, bool liveAtoClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "SOV-001", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be SOV-001, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateFrameworks(matrix.Frameworks));
        failures.AddRange(EvaluateRequired(matrix.ImpactCategorization, "impactCategorization"));
        failures.AddRange(EvaluateCoverage(matrix.ControlCoverage));
        failures.AddRange(EvaluateOwnership(matrix.OwnershipEvidence));
        failures.AddRange(EvaluatePoam(matrix.Poam));
        failures.AddRange(EvaluateExceptions(matrix.Exceptions));
        failures.AddRange(EvaluateRequired(matrix.AuthorizationBoundary, "authorizationBoundary"));
        failures.AddRange(EvaluateReassessment(matrix.Reassessment));
        failures.AddRange(EvaluateIndependentAssessment(matrix.IndependentAssessment));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.ControlBaselinePolicyVersion));

        if (liveAtoClaim)
            failures.Add("LiveAtoClaim forbidden until SOV-001a live independent ATO");

        return failures;
    }

    public static List<string> EvaluateBlockers(SovBaselineMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("STR-002", "Satisfied"),
                     ("SUP-001", "Satisfied"),
                     ("GOV-001", "Satisfied"),
                     ("live-independent-ato", "Blocked"),
                     ("continuous-assessment-workers", "Blocked"),
                     ("live-poam-system", "Blocked"),
                     ("SYR-001", "NotStarted"),
                     ("SOV-002", "Satisfied"),
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

    public static List<string> EvaluateFrameworks(SovBaselineFrameworks? frameworks)
    {
        var failures = new List<string>();
        if (frameworks is null)
        {
            failures.Add("frameworks missing");
            return failures;
        }

        if (!frameworks.Required)
            failures.Add("frameworks.required must be true");
        if (!frameworks.LocalLawTailoringRequired)
            failures.Add("localLawTailoringRequired must be true");
        foreach (var need in RequiredCrosswalk)
        {
            if (!frameworks.Crosswalk.Any(t => string.Equals(t, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing crosswalk {need}");
        }

        return failures;
    }

    public static List<string> EvaluateRequired(SovBaselineRequiredFlag? section, string name)
    {
        if (section is null)
            return [$"{name} missing"];
        if (!section.Required)
            return [$"{name}.required must be true"];
        return [];
    }

    public static List<string> EvaluateCoverage(SovBaselineControlCoverage? coverage)
    {
        var failures = new List<string>();
        if (coverage is null)
        {
            failures.Add("controlCoverage missing");
            return failures;
        }

        if (!coverage.Required)
            failures.Add("controlCoverage.required must be true");
        if (!coverage.FullCoverageRequired)
            failures.Add("fullCoverageRequired must be true");
        if (!coverage.SiteReasonRequiredForNa)
            failures.Add("siteReasonRequiredForNa must be true");
        foreach (var need in RequiredStatuses)
        {
            if (!coverage.AllowedStatuses.Any(t => string.Equals(t, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"missing allowedStatus {need}");
        }

        return failures;
    }

    public static string? ValidateCoverage(bool statusKnown, bool siteReasonPresentIfNa, SovBaselineControlCoverage? coverage)
    {
        if (!statusKnown && (coverage?.FullCoverageRequired ?? true))
            return "control coverage status required";
        if (!siteReasonPresentIfNa && (coverage?.SiteReasonRequiredForNa ?? true))
            return "not-applicable requires site reason";
        return null;
    }

    public static List<string> EvaluateOwnership(SovBaselineOwnershipEvidence? ownership)
    {
        var failures = new List<string>();
        if (ownership is null)
        {
            failures.Add("ownershipEvidence missing");
            return failures;
        }

        if (!ownership.Required)
            failures.Add("ownershipEvidence.required must be true");
        if (!ownership.OwnerRequired)
            failures.Add("ownerRequired must be true");
        if (!ownership.EvidenceRequired)
            failures.Add("evidenceRequired must be true");
        if (!ownership.TestRefRequired)
            failures.Add("testRefRequired must be true");
        if (!ownership.FrequencyRequired)
            failures.Add("frequencyRequired must be true");
        return failures;
    }

    public static string? ValidateControlOwnership(bool hasOwner, bool hasEvidence, SovBaselineOwnershipEvidence? ownership)
    {
        if (!hasOwner && (ownership?.OwnerRequired ?? true))
            return "control owner required";
        if (!hasEvidence && (ownership?.EvidenceRequired ?? true))
            return "control evidence required";
        return null;
    }

    public static List<string> EvaluatePoam(SovBaselinePoam? poam)
    {
        var failures = new List<string>();
        if (poam is null)
        {
            failures.Add("poam missing");
            return failures;
        }

        if (!poam.Required)
            failures.Add("poam.required must be true");
        if (!poam.DatedRemediationRequired)
            failures.Add("datedRemediationRequired must be true");
        return failures;
    }

    public static string? ValidatePoam(bool datedRemediationPresent, SovBaselinePoam? poam)
    {
        if (!datedRemediationPresent && (poam?.DatedRemediationRequired ?? true))
            return "POA&M dated remediation required";
        return null;
    }

    public static List<string> EvaluateExceptions(SovBaselineExceptions? exceptions)
    {
        var failures = new List<string>();
        if (exceptions is null)
        {
            failures.Add("exceptions missing");
            return failures;
        }

        if (!exceptions.Required)
            failures.Add("exceptions.required must be true");
        if (!exceptions.SignedExceptionRequired)
            failures.Add("signedExceptionRequired must be true");
        return failures;
    }

    public static string? ValidateException(bool signed, SovBaselineExceptions? exceptions)
    {
        if (!signed && (exceptions?.SignedExceptionRequired ?? true))
            return "exception must be signed";
        return null;
    }

    public static string? ValidateAuthorizationBoundary(bool boundaryPresent, SovBaselineRequiredFlag? boundary)
    {
        if (!boundaryPresent && (boundary?.Required ?? true))
            return "authorization boundary required";
        return null;
    }

    public static List<string> EvaluateReassessment(SovBaselineReassessment? reassessment)
    {
        var failures = new List<string>();
        if (reassessment is null)
        {
            failures.Add("reassessment missing");
            return failures;
        }

        if (!reassessment.Required)
            failures.Add("reassessment.required must be true");
        if (!reassessment.AfterMaterialChangeRequired)
            failures.Add("afterMaterialChangeRequired must be true");
        return failures;
    }

    public static string? ValidateReassessment(bool triggeredAfterMaterialChange, SovBaselineReassessment? reassessment)
    {
        if (!triggeredAfterMaterialChange && (reassessment?.AfterMaterialChangeRequired ?? true))
            return "reassessment after material change required";
        return null;
    }

    public static List<string> EvaluateIndependentAssessment(SovBaselineIndependentAssessment? assessment)
    {
        var failures = new List<string>();
        if (assessment is null)
        {
            failures.Add("independentAssessment missing");
            return failures;
        }

        if (!assessment.Required)
            failures.Add("independentAssessment.required must be true");
        if (!assessment.LabStubAllowed)
            failures.Add("labStubAllowed must be true");
        if (!assessment.LiveAtoForbidden)
            failures.Add("liveAtoForbidden must be true");
        return failures;
    }

    public static string? ValidateIndependentAssessment(bool samplePresent)
    {
        if (!samplePresent)
            return "independent assessment sample required";
        return null;
    }

    public static List<string> EvaluateAudit(SovBaselineAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, SovBaselineAudit? audit)
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

    public static List<string> EvaluateMockSandbox(SovBaselineMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveAtoForbidden)
            failures.Add("liveAtoForbidden must be true");
        if (!sandbox.LivePoamSystemForbidden)
            failures.Add("livePoamSystemForbidden must be true");
        if (!sandbox.LiveContinuousAssessmentForbidden)
            failures.Add("liveContinuousAssessmentForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(SovBaselineSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(SovBaselineSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.ControlBaselinePolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
