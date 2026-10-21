using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Web.ViewModels;

public static class AssetDetailsFormKeys
{
    public const string Assign = "assign";
    public const string Return = "return";
    public const string Loss = "loss";
}

public sealed class AssignToEmployeeDraft
{
    public Guid EmployeeId { get; set; }
    public DateTime? DueBackDate { get; set; }
    public AssetHandoverCondition? HandoverCondition { get; set; }
    public string? HandoverConditionNotes { get; set; }
    public string? EvidenceRef { get; set; }
    public Guid? HandoverFileId { get; set; }
}

public sealed class ReturnAssignmentDraft
{
    public string? EvidenceRef { get; set; }
    public Guid? ReturnFileId { get; set; }
}

public sealed class ReportLossDraft
{
    public AssetLossReason? Reason { get; set; }
    public AssetLossContext? Context { get; set; }
    public string? Notes { get; set; }
    public string? EvidenceRef { get; set; }
    public Guid? LossFileId { get; set; }
}

public sealed class AssetDetailsFormState
{
    public AssignToEmployeeDraft? Assign { get; init; }
    public ReturnAssignmentDraft? Return { get; init; }
    public ReportLossDraft? Loss { get; init; }
    public string? FormErrorAssign { get; init; }
    public string? FormErrorReturn { get; init; }
    public string? FormErrorLoss { get; init; }
    public string? ScrollToForm { get; init; }
}
