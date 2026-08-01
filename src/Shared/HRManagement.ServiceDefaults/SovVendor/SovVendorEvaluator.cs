namespace HRManagement.ServiceDefaults.SovVendor;

public static class SovVendorEvaluator
{
    private static readonly string[] RequiredAuditFields =
    [
        "artifactId", "ownershipType", "exportRef", "alternateRef", "rebuildRef", "actor", "occurredAt",
        "evidenceRef", "outcome", "destructionRef"
    ];

    public static List<string> Evaluate(SovVendorMatrixDocument matrix, bool liveVendorClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "SOV-007", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be SOV-007, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateOwnership(matrix.Ownership));
        failures.AddRange(EvaluateEscrowLicenses(matrix.EscrowLicenses));
        failures.AddRange(EvaluateOpenExport(matrix.OpenExport));
        failures.AddRange(EvaluateCriticalAlternates(matrix.CriticalAlternates));
        failures.AddRange(EvaluateCleanRoomRebuild(matrix.CleanRoomRebuild));
        failures.AddRange(EvaluateKnowledgeTransfer(matrix.KnowledgeTransfer));
        failures.AddRange(EvaluateExitAssistance(matrix.ExitAssistance));
        failures.AddRange(EvaluateDestructionAttestation(matrix.DestructionAttestation));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.VendorExitPolicyVersion));

        if (liveVendorClaim)
            failures.Add("LiveVendorClaim forbidden until SOV-007a live clean-room rebuild");

        return failures;
    }

    public static List<string> EvaluateBlockers(SovVendorMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("SUP-001", "Satisfied"),
                     ("OPS-005", "Satisfied"),
                     ("DB-001", "Satisfied"),
                     ("SOV-006", "Satisfied"),
                     ("live-clean-room-rebuild", "Blocked"),
                     ("live-vendor-exit-drill", "Blocked"),
                     ("live-full-export-import", "Blocked"),
                     ("live-vendor-destruction-attestation", "Blocked"),
                     ("VEN-001", "NotStarted"),
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

    public static List<string> EvaluateOwnership(SovVendorOwnership? ownership)
    {
        var failures = new List<string>();
        if (ownership is null)
        {
            failures.Add("ownership missing");
            return failures;
        }

        if (!ownership.Required)
            failures.Add("ownership.required must be true");
        if (!ownership.IpSourceBuildIacSbomDocsRequired)
            failures.Add("ipSourceBuildIacSbomDocsRequired must be true");
        return failures;
    }

    public static string? ValidateOwnership(bool ownedArtifactsPresent, SovVendorOwnership? ownership)
    {
        if (!ownedArtifactsPresent && (ownership?.IpSourceBuildIacSbomDocsRequired ?? true))
            return "owned artifacts (IP/source/build/IaC/SBOM/docs) required";
        return null;
    }

    public static List<string> EvaluateEscrowLicenses(SovVendorEscrowLicenses? escrow)
    {
        var failures = new List<string>();
        if (escrow is null)
        {
            failures.Add("escrowLicenses missing");
            return failures;
        }

        if (!escrow.Required)
            failures.Add("escrowLicenses.required must be true");
        if (!escrow.EscrowRequired)
            failures.Add("escrowRequired must be true");
        if (!escrow.LicenseRightsRequired)
            failures.Add("licenseRightsRequired must be true");
        if (!escrow.SubcontractorDisclosureRequired)
            failures.Add("subcontractorDisclosureRequired must be true");
        return failures;
    }

    public static string? ValidateEscrow(bool escrowPresent, bool licenseRightsPresent, SovVendorEscrowLicenses? escrow)
    {
        if (!escrowPresent && (escrow?.EscrowRequired ?? true))
            return "escrow required";
        if (!licenseRightsPresent && (escrow?.LicenseRightsRequired ?? true))
            return "license rights required";
        return null;
    }

    public static List<string> EvaluateOpenExport(SovVendorOpenExport? export)
    {
        var failures = new List<string>();
        if (export is null)
        {
            failures.Add("openExport missing");
            return failures;
        }

        if (!export.Required)
            failures.Add("openExport.required must be true");
        if (!export.FullDataExportRequired)
            failures.Add("fullDataExportRequired must be true");
        if (!export.AttachmentsExportRequired)
            failures.Add("attachmentsExportRequired must be true");
        if (!export.ContractsExportRequired)
            failures.Add("contractsExportRequired must be true");
        if (!export.DocumentedFormatsRequired)
            failures.Add("documentedFormatsRequired must be true");
        return failures;
    }

    public static string? ValidateOpenExport(bool fullExportPresent, bool formatsDocumented, SovVendorOpenExport? export)
    {
        if (!fullExportPresent && (export?.FullDataExportRequired ?? true))
            return "full data/attachments/contracts export required";
        if (!formatsDocumented && (export?.DocumentedFormatsRequired ?? true))
            return "documented export formats required";
        return null;
    }

    public static List<string> EvaluateCriticalAlternates(SovVendorCriticalAlternates? alternates)
    {
        var failures = new List<string>();
        if (alternates is null)
        {
            failures.Add("criticalAlternates missing");
            return failures;
        }

        if (!alternates.Required)
            failures.Add("criticalAlternates.required must be true");
        if (!alternates.NoCriticalWithoutAlternateOrException)
            failures.Add("noCriticalWithoutAlternateOrException must be true");
        return failures;
    }

    public static string? ValidateCriticalAlternates(bool criticalWithoutAlternate, SovVendorCriticalAlternates? alternates)
    {
        if (criticalWithoutAlternate && (alternates?.NoCriticalWithoutAlternateOrException ?? true))
            return "critical component without alternate/exception forbidden";
        return null;
    }

    public static List<string> EvaluateCleanRoomRebuild(SovVendorCleanRoomRebuild? cleanRoom)
    {
        var failures = new List<string>();
        if (cleanRoom is null)
        {
            failures.Add("cleanRoomRebuild missing");
            return failures;
        }

        if (!cleanRoom.Required)
            failures.Add("cleanRoomRebuild.required must be true");
        if (!cleanRoom.OwnedArtifactsOnlyRequired)
            failures.Add("ownedArtifactsOnlyRequired must be true");
        if (!cleanRoom.AlternateSiteRequired)
            failures.Add("alternateSiteRequired must be true");
        return failures;
    }

    public static string? ValidateCleanRoom(bool ownedArtifactsOnly, bool alternateSitePresent, SovVendorCleanRoomRebuild? cleanRoom)
    {
        if (!ownedArtifactsOnly && (cleanRoom?.OwnedArtifactsOnlyRequired ?? true))
            return "clean-room rebuild requires owned artifacts only";
        if (!alternateSitePresent && (cleanRoom?.AlternateSiteRequired ?? true))
            return "clean-room alternate site required";
        return null;
    }

    public static List<string> EvaluateKnowledgeTransfer(SovVendorKnowledgeTransfer? knowledge)
    {
        var failures = new List<string>();
        if (knowledge is null)
        {
            failures.Add("knowledgeTransfer missing");
            return failures;
        }

        if (!knowledge.Required)
            failures.Add("knowledgeTransfer.required must be true");
        return failures;
    }

    public static string? ValidateKnowledgeTransfer(bool transferPresent, SovVendorKnowledgeTransfer? knowledge)
    {
        if (!transferPresent && (knowledge?.Required ?? true))
            return "knowledge transfer required";
        return null;
    }

    public static List<string> EvaluateExitAssistance(SovVendorExitAssistance? assistance)
    {
        var failures = new List<string>();
        if (assistance is null)
        {
            failures.Add("exitAssistance missing");
            return failures;
        }

        if (!assistance.Required)
            failures.Add("exitAssistance.required must be true");
        return failures;
    }

    public static string? ValidateExitAssistance(bool assistancePresent, SovVendorExitAssistance? assistance)
    {
        if (!assistancePresent && (assistance?.Required ?? true))
            return "exit assistance required";
        return null;
    }

    public static List<string> EvaluateDestructionAttestation(SovVendorDestructionAttestation? destruction)
    {
        var failures = new List<string>();
        if (destruction is null)
        {
            failures.Add("destructionAttestation missing");
            return failures;
        }

        if (!destruction.Required)
            failures.Add("destructionAttestation.required must be true");
        if (!destruction.PriorVendorProofRequired)
            failures.Add("priorVendorProofRequired must be true");
        return failures;
    }

    public static string? ValidateDestruction(bool attested, SovVendorDestructionAttestation? destruction)
    {
        if (!attested && (destruction?.PriorVendorProofRequired ?? true))
            return "prior-vendor destruction attestation required";
        return null;
    }

    public static List<string> EvaluateAudit(SovVendorAudit? audit)
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

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, SovVendorAudit? audit)
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

    public static List<string> EvaluateMockSandbox(SovVendorMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveCleanRoomRebuildForbidden)
            failures.Add("liveCleanRoomRebuildForbidden must be true");
        if (!sandbox.LiveVendorExitDrillForbidden)
            failures.Add("liveVendorExitDrillForbidden must be true");
        if (!sandbox.LiveFullExportImportForbidden)
            failures.Add("liveFullExportImportForbidden must be true");
        if (!sandbox.LiveVendorDestructionAttestationForbidden)
            failures.Add("liveVendorDestructionAttestationForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(SovVendorSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(SovVendorSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.VendorExitPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
