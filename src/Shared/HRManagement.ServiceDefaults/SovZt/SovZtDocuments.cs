using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.SovZt;

public sealed class SovZtMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("zeroTrustPolicyVersion")]
    public string? ZeroTrustPolicyVersion { get; set; }

    [JsonPropertyName("noStandingPrivilege")]
    public SovZtNoStandingPrivilege? NoStandingPrivilege { get; set; }

    [JsonPropertyName("pamJit")]
    public SovZtPamJit? PamJit { get; set; }

    [JsonPropertyName("phishingResistantMfa")]
    public SovZtPhishingResistantMfa? PhishingResistantMfa { get; set; }

    [JsonPropertyName("adminWorkstations")]
    public SovZtAdminWorkstations? AdminWorkstations { get; set; }

    [JsonPropertyName("sessionRecord")]
    public SovZtRequiredFlag? SessionRecord { get; set; }

    [JsonPropertyName("approvals")]
    public SovZtRequiredFlag? Approvals { get; set; }

    [JsonPropertyName("breakGlass")]
    public SovZtBreakGlass? BreakGlass { get; set; }

    [JsonPropertyName("serviceAdminSeparation")]
    public SovZtRequiredFlag? ServiceAdminSeparation { get; set; }

    [JsonPropertyName("privilegedTargets")]
    public SovZtPrivilegedTargets? PrivilegedTargets { get; set; }

    [JsonPropertyName("accessRecertification")]
    public SovZtAccessRecertification? AccessRecertification { get; set; }

    [JsonPropertyName("audit")]
    public SovZtAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public SovZtMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public SovZtSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<SovZtBlocker> Blockers { get; set; } = [];
}

public sealed class SovZtRequiredFlag
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class SovZtNoStandingPrivilege
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("sharedRootForbidden")]
    public bool SharedRootForbidden { get; set; }

    [JsonPropertyName("permanentAdminForbidden")]
    public bool PermanentAdminForbidden { get; set; }
}

public sealed class SovZtPamJit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("temporaryOnly")]
    public bool TemporaryOnly { get; set; }

    [JsonPropertyName("reasonRequired")]
    public bool ReasonRequired { get; set; }

    [JsonPropertyName("approvalRequired")]
    public bool ApprovalRequired { get; set; }

    [JsonPropertyName("jeaRequired")]
    public bool JeaRequired { get; set; }
}

public sealed class SovZtPhishingResistantMfa
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("adminPathsRequired")]
    public bool AdminPathsRequired { get; set; }
}

public sealed class SovZtAdminWorkstations
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("ordinaryDeviceForbidden")]
    public bool OrdinaryDeviceForbidden { get; set; }
}

public sealed class SovZtBreakGlass
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("immediateAlertRequired")]
    public bool ImmediateAlertRequired { get; set; }

    [JsonPropertyName("postUseReviewRequired")]
    public bool PostUseReviewRequired { get; set; }
}

public sealed class SovZtPrivilegedTargets
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("targets")]
    public List<string> Targets { get; set; } = [];
}

public sealed class SovZtAccessRecertification
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("quarterlyRequired")]
    public bool QuarterlyRequired { get; set; }

    [JsonPropertyName("dormantOrphanZeroRequired")]
    public bool DormantOrphanZeroRequired { get; set; }
}

public sealed class SovZtAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class SovZtMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("livePamForbidden")]
    public bool LivePamForbidden { get; set; }

    [JsonPropertyName("liveAdminWorkstationForbidden")]
    public bool LiveAdminWorkstationForbidden { get; set; }

    [JsonPropertyName("liveBreakGlassSocForbidden")]
    public bool LiveBreakGlassSocForbidden { get; set; }

    [JsonPropertyName("liveAccessRecertificationForbidden")]
    public bool LiveAccessRecertificationForbidden { get; set; }
}

public sealed class SovZtSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("zeroTrustPolicyVersion")]
    public string? ZeroTrustPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class SovZtBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
