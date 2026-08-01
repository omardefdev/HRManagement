namespace HRManagement.ServiceDefaults.ArcReference;

public static class ArcReferenceEvaluator
{
    private static readonly string[] RequiredAuditFields =
    [
        "contextId", "ownerRole", "interactionStyle", "dataOwnerRef", "siteProfileRef", "topologyRef",
        "couplingScanRef", "fitnessRef", "actor", "occurredAt", "evidenceRef", "outcome", "rationaleRef"
    ];

    public static List<string> Evaluate(ArcReferenceMatrixDocument matrix, bool liveReferenceClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "ARC-002", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be ARC-002, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateBoundedContexts(matrix.BoundedContexts));
        failures.AddRange(EvaluateSyncVsEvent(matrix.SyncVsEvent));
        failures.AddRange(EvaluateDataOwnership(matrix.DataOwnership));
        failures.AddRange(EvaluateMultiSiteProfiles(matrix.MultiSiteProfiles));
        failures.AddRange(EvaluateTenancyBoundaries(matrix.TenancyBoundaries));
        failures.AddRange(EvaluateControlDataPlanes(matrix.ControlDataPlanes));
        failures.AddRange(EvaluateSupportedTopologies(matrix.SupportedTopologies));
        failures.AddRange(EvaluateCouplingGuardrails(matrix.CouplingGuardrails));
        failures.AddRange(EvaluateFitnessEnvelope(matrix.FitnessEnvelope));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.ReferenceArchitecturePolicyVersion));

        if (liveReferenceClaim)
            failures.Add("LiveReferenceClaim forbidden until ARC-002a live two-site deployment");

        return failures;
    }

    public static List<string> EvaluateBlockers(ArcReferenceMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("ARC-001", "Satisfied"),
                     ("TEN-001", "Satisfied"),
                     ("INT-001", "Satisfied"),
                     ("OPS-005", "Satisfied"),
                     ("live-two-site-deployment", "Blocked"),
                     ("live-architecture-fitness-envelope", "Blocked"),
                     ("live-coupling-boundary-scan", "Blocked"),
                     ("live-reference-implementation-signoff", "Blocked"),
                     ("QA-005", "Satisfied"),
                     ("PHY-001", "Satisfied")
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

    public static List<string> EvaluateBoundedContexts(ArcReferenceBoundedContexts? contexts)
    {
        var failures = new List<string>();
        if (contexts is null)
        {
            failures.Add("boundedContexts missing");
            return failures;
        }

        if (!contexts.Required)
            failures.Add("boundedContexts.required must be true");
        if (!contexts.EveryContextHasOwnerAndRationaleRequired)
            failures.Add("everyContextHasOwnerAndRationaleRequired must be true");
        if (!contexts.NoUnjustifiedDistributedComplexityRequired)
            failures.Add("noUnjustifiedDistributedComplexityRequired must be true");
        return failures;
    }

    public static string? ValidateBoundedContexts(
        bool everyContextOwned,
        bool unjustifiedDistributionPresent,
        ArcReferenceBoundedContexts? contexts)
    {
        if (!everyContextOwned && (contexts?.EveryContextHasOwnerAndRationaleRequired ?? true))
            return "every bounded context must have owner and rationale";
        if (unjustifiedDistributionPresent && (contexts?.NoUnjustifiedDistributedComplexityRequired ?? true))
            return "unjustified distributed complexity forbidden";
        return null;
    }

    public static List<string> EvaluateSyncVsEvent(ArcReferenceSyncVsEvent? sync)
    {
        var failures = new List<string>();
        if (sync is null)
        {
            failures.Add("syncVsEvent missing");
            return failures;
        }

        if (!sync.Required)
            failures.Add("syncVsEvent.required must be true");
        if (!sync.InteractionStyleDocumentedRequired)
            failures.Add("interactionStyleDocumentedRequired must be true");
        return failures;
    }

    public static List<string> EvaluateDataOwnership(ArcReferenceDataOwnership? ownership)
    {
        var failures = new List<string>();
        if (ownership is null)
        {
            failures.Add("dataOwnership missing");
            return failures;
        }

        if (!ownership.Required)
            failures.Add("dataOwnership.required must be true");
        if (!ownership.SingleWriterPerAggregateRequired)
            failures.Add("singleWriterPerAggregateRequired must be true");
        return failures;
    }

    public static string? ValidateDataOwnership(bool singleWriterPresent, ArcReferenceDataOwnership? ownership)
    {
        if (!singleWriterPresent && (ownership?.SingleWriterPerAggregateRequired ?? true))
            return "single writer per aggregate required";
        return null;
    }

    public static List<string> EvaluateMultiSiteProfiles(ArcReferenceMultiSiteProfiles? multiSite)
    {
        var failures = new List<string>();
        if (multiSite is null)
        {
            failures.Add("multiSiteProfiles missing");
            return failures;
        }

        if (!multiSite.Required)
            failures.Add("multiSiteProfiles.required must be true");
        if (!multiSite.SameArtifactPrimaryAndDrRequired)
            failures.Add("sameArtifactPrimaryAndDrRequired must be true");
        if (!multiSite.PrimaryAndDrProfilesRequired)
            failures.Add("primaryAndDrProfilesRequired must be true");
        return failures;
    }

    public static string? ValidateMultiSite(
        bool sameArtifactPrimaryAndDr,
        bool primaryAndDrProfilesPresent,
        ArcReferenceMultiSiteProfiles? multiSite)
    {
        if (!sameArtifactPrimaryAndDr && (multiSite?.SameArtifactPrimaryAndDrRequired ?? true))
            return "same artifact for primary and DR required";
        if (!primaryAndDrProfilesPresent && (multiSite?.PrimaryAndDrProfilesRequired ?? true))
            return "primary and DR site profiles required";
        return null;
    }

    public static List<string> EvaluateTenancyBoundaries(ArcReferenceTenancyBoundaries? tenancy)
    {
        var failures = new List<string>();
        if (tenancy is null)
        {
            failures.Add("tenancyBoundaries missing");
            return failures;
        }

        if (!tenancy.Required)
            failures.Add("tenancyBoundaries.required must be true");
        if (!tenancy.TenantModelAlignedRequired)
            failures.Add("tenantModelAlignedRequired must be true");
        return failures;
    }

    public static string? ValidateTenancy(bool tenantModelAligned, ArcReferenceTenancyBoundaries? tenancy)
    {
        if (!tenantModelAligned && (tenancy?.TenantModelAlignedRequired ?? true))
            return "tenant model alignment required";
        return null;
    }

    public static List<string> EvaluateControlDataPlanes(ArcReferenceControlDataPlanes? planes)
    {
        var failures = new List<string>();
        if (planes is null)
        {
            failures.Add("controlDataPlanes missing");
            return failures;
        }

        if (!planes.Required)
            failures.Add("controlDataPlanes.required must be true");
        if (!planes.PlanesSeparatedRequired)
            failures.Add("planesSeparatedRequired must be true");
        return failures;
    }

    public static string? ValidatePlanes(bool planesSeparated, ArcReferenceControlDataPlanes? planes)
    {
        if (!planesSeparated && (planes?.PlanesSeparatedRequired ?? true))
            return "control/data planes separation required";
        return null;
    }

    public static List<string> EvaluateSupportedTopologies(ArcReferenceSupportedTopologies? topologies)
    {
        var failures = new List<string>();
        if (topologies is null)
        {
            failures.Add("supportedTopologies missing");
            return failures;
        }

        if (!topologies.Required)
            failures.Add("supportedTopologies.required must be true");
        if (!topologies.UnsupportedTopologyRejectedRequired)
            failures.Add("unsupportedTopologyRejectedRequired must be true");
        if (!topologies.EnvelopeValidatedRequired)
            failures.Add("envelopeValidatedRequired must be true");
        return failures;
    }

    public static string? ValidateTopology(
        bool unsupportedTopologyPresent,
        bool envelopeValidated,
        ArcReferenceSupportedTopologies? topologies)
    {
        if (unsupportedTopologyPresent && (topologies?.UnsupportedTopologyRejectedRequired ?? true))
            return "unsupported topology forbidden";
        if (!envelopeValidated && (topologies?.EnvelopeValidatedRequired ?? true))
            return "topology must validate national envelope";
        return null;
    }

    public static List<string> EvaluateCouplingGuardrails(ArcReferenceCouplingGuardrails? coupling)
    {
        var failures = new List<string>();
        if (coupling is null)
        {
            failures.Add("couplingGuardrails missing");
            return failures;
        }

        if (!coupling.Required)
            failures.Add("couplingGuardrails.required must be true");
        if (!coupling.NoDirectDbCrossBoundaryRequired)
            failures.Add("noDirectDbCrossBoundaryRequired must be true");
        if (!coupling.NoDomainCouplingAcrossContextsRequired)
            failures.Add("noDomainCouplingAcrossContextsRequired must be true");
        return failures;
    }

    public static string? ValidateCoupling(
        bool directDbCrossBoundary,
        bool domainCouplingAcrossContexts,
        ArcReferenceCouplingGuardrails? coupling)
    {
        if (directDbCrossBoundary && (coupling?.NoDirectDbCrossBoundaryRequired ?? true))
            return "direct DB cross-boundary forbidden";
        if (domainCouplingAcrossContexts && (coupling?.NoDomainCouplingAcrossContextsRequired ?? true))
            return "domain coupling across contexts forbidden";
        return null;
    }

    public static List<string> EvaluateFitnessEnvelope(ArcReferenceFitnessEnvelope? fitness)
    {
        var failures = new List<string>();
        if (fitness is null)
        {
            failures.Add("fitnessEnvelope missing");
            return failures;
        }

        if (!fitness.Required)
            failures.Add("fitnessEnvelope.required must be true");
        if (!fitness.ReferenceImplementationRequired)
            failures.Add("referenceImplementationRequired must be true");
        if (!fitness.LabFitnessHarnessRequired)
            failures.Add("labFitnessHarnessRequired must be true");
        return failures;
    }

    public static string? ValidateFitness(
        bool referenceImplementationPresent,
        bool labFitnessHarnessPresent,
        ArcReferenceFitnessEnvelope? fitness)
    {
        if (!referenceImplementationPresent && (fitness?.ReferenceImplementationRequired ?? true))
            return "reference implementation required";
        if (!labFitnessHarnessPresent && (fitness?.LabFitnessHarnessRequired ?? true))
            return "lab fitness harness required";
        return null;
    }

    public static List<string> EvaluateAudit(ArcReferenceAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, ArcReferenceAudit? audit)
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

    public static List<string> EvaluateMockSandbox(ArcReferenceMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveTwoSiteDeploymentForbidden)
            failures.Add("liveTwoSiteDeploymentForbidden must be true");
        if (!sandbox.LiveArchitectureFitnessEnvelopeForbidden)
            failures.Add("liveArchitectureFitnessEnvelopeForbidden must be true");
        if (!sandbox.LiveCouplingBoundaryScanForbidden)
            failures.Add("liveCouplingBoundaryScanForbidden must be true");
        if (!sandbox.LiveReferenceImplementationSignoffForbidden)
            failures.Add("liveReferenceImplementationSignoffForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ArcReferenceSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ArcReferenceSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.ReferenceArchitecturePolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
