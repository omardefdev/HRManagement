namespace HRManagement.SharedKernel.Evidence;

/// <summary>
/// EVD-002: Tracks a forensic investigation case with custody chain and legal hold.
/// </summary>
public class ForensicCase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string CaseNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ForensicCaseSeverity Severity { get; set; } = ForensicCaseSeverity.Medium;

    public ForensicCaseStatus Status { get; set; } = ForensicCaseStatus.Open;

    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClosedAt { get; set; }

    public string LeadInvestigator { get; set; } = string.Empty;

    public bool LegalHoldActive { get; set; }

    public string? RelatedEvidenceIdsJson { get; set; }

    public string? Notes { get; set; }
}

public enum ForensicCaseSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum ForensicCaseStatus
{
    Open,
    InProgress,
    Closed,
    Archived
}
