using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ExtTreasury;

public sealed class ExtTreasuryMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("treasuryPolicyVersion")]
    public string? TreasuryPolicyVersion { get; set; }

    [JsonPropertyName("approvedRunRequired")]
    public bool ApprovedRunRequired { get; set; }

    [JsonPropertyName("runStatusesAllowed")]
    public List<string> RunStatusesAllowed { get; set; } = [];

    [JsonPropertyName("paymentOrder")]
    public ExtTreasuryPaymentOrder? PaymentOrder { get; set; }

    [JsonPropertyName("signatures")]
    public ExtTreasurySignatures? Signatures { get; set; }

    [JsonPropertyName("channel")]
    public ExtTreasuryChannel? Channel { get; set; }

    [JsonPropertyName("bankAccountVerification")]
    public ExtTreasuryBankVerification? BankAccountVerification { get; set; }

    [JsonPropertyName("settlement")]
    public ExtTreasurySettlement? Settlement { get; set; }

    [JsonPropertyName("reconciliation")]
    public ExtTreasuryReconciliation? Reconciliation { get; set; }

    [JsonPropertyName("exactlyOnce")]
    public ExtTreasuryExactlyOnce? ExactlyOnce { get; set; }

    [JsonPropertyName("rejectCorrectionWorkflow")]
    public ExtTreasuryRejectCorrection? RejectCorrectionWorkflow { get; set; }

    [JsonPropertyName("cutoffCalendar")]
    public ExtTreasuryCutoffCalendar? CutoffCalendar { get; set; }

    [JsonPropertyName("manualFiles")]
    public ExtTreasuryManualFiles? ManualFiles { get; set; }

    [JsonPropertyName("audit")]
    public ExtTreasuryAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ExtTreasuryMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ExtTreasurySigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ExtTreasuryBlocker> Blockers { get; set; } = [];
}

public sealed class ExtTreasuryPaymentOrder
{
    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];

    [JsonPropertyName("bindingToRunId")]
    public bool BindingToRunId { get; set; }
}

public sealed class ExtTreasurySignatures
{
    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("livePkiRequiresExt004")]
    public bool LivePkiRequiresExt004 { get; set; }
}

public sealed class ExtTreasuryChannel
{
    [JsonPropertyName("fileShapes")]
    public List<string> FileShapes { get; set; } = [];

    [JsonPropertyName("apiShapes")]
    public List<string> ApiShapes { get; set; } = [];

    [JsonPropertyName("ackCodes")]
    public List<string> AckCodes { get; set; } = [];

    [JsonPropertyName("returnRejectCodes")]
    public List<string> ReturnRejectCodes { get; set; } = [];
}

public sealed class ExtTreasuryBankVerification
{
    [JsonPropertyName("requiredBeforeSettle")]
    public bool RequiredBeforeSettle { get; set; }
}

public sealed class ExtTreasurySettlement
{
    [JsonPropertyName("states")]
    public List<string> States { get; set; } = [];
}

public sealed class ExtTreasuryReconciliation
{
    [JsonPropertyName("totalsMustMatch")]
    public bool TotalsMustMatch { get; set; }

    [JsonPropertyName("lineCountMustMatch")]
    public bool LineCountMustMatch { get; set; }
}

public sealed class ExtTreasuryExactlyOnce
{
    [JsonPropertyName("idempotencyKeyRequired")]
    public bool IdempotencyKeyRequired { get; set; }

    [JsonPropertyName("duplicatePolicy")]
    public string DuplicatePolicy { get; set; } = "";
}

public sealed class ExtTreasuryRejectCorrection
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("queue")]
    public string? Queue { get; set; }
}

public sealed class ExtTreasuryCutoffCalendar
{
    [JsonPropertyName("cutoffRequired")]
    public bool CutoffRequired { get; set; }

    [JsonPropertyName("contingencyPath")]
    public string? ContingencyPath { get; set; }
}

public sealed class ExtTreasuryManualFiles
{
    [JsonPropertyName("unauditedForbidden")]
    public bool UnauditedForbidden { get; set; }

    [JsonPropertyName("auditRequiredFields")]
    public List<string> AuditRequiredFields { get; set; } = [];
}

public sealed class ExtTreasuryAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ExtTreasuryMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveTreasuryForbidden")]
    public bool LiveTreasuryForbidden { get; set; }

    [JsonPropertyName("portJoin")]
    public string? PortJoin { get; set; }
}

public sealed class ExtTreasurySigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("treasuryPolicyVersion")]
    public string? TreasuryPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ExtTreasuryBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
