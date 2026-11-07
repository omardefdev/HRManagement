namespace Admin.Logistics.Domain.Entities;

/// <summary>
/// PLAN-45: what a custody assignment and a loss report share while they wait for an approval. The requester is <c>CreatedBy</c>.
/// </summary>
public interface IAssetApprovalRequest
{
    Guid Id { get; }

    Guid TenantId { get; }

    Guid AssetId { get; }

    Guid? CreatedBy { get; }

    /// <summary>For display only, kept so that a start by the retry job still names the requester.</summary>
    string? RequestedByName { get; }

    Guid? WorkflowInstanceId { get; set; }

    int ApprovalStartAttempts { get; set; }

    string? ApprovalStartLastError { get; set; }

    DateTime? ApprovalStartLastAttemptAt { get; set; }

    bool NeedsIntervention { get; set; }

    DateTime? WithdrawnAt { get; set; }

    Guid? WithdrawnBy { get; set; }

    string? WithdrawnReason { get; set; }
}
