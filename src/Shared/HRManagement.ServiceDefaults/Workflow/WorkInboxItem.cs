using System.ComponentModel.DataAnnotations;

namespace HRManagement.ServiceDefaults.Workflow;

/// <summary>WF-002: a unified work inbox item aggregating tasks/approvals from all sources.</summary>
public class WorkInboxItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid AssigneeId { get; set; }

    [MaxLength(50)]
    public string? TeamId { get; set; }

    [Required, MaxLength(100)]
    public string SourceSystem { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string SourceReferenceId { get; set; } = string.Empty;

    public WorkItemType ItemType { get; set; }

    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Normal;

    public WorkItemStatus Status { get; set; } = WorkItemStatus.Pending;

    [MaxLength(500)]
    public string? Title { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? DeepLink { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DueDate { get; set; }

    public DateTime? CompletedAt { get; set; }

    public bool IsEscalated { get; set; }

    [MaxLength(200)]
    public string? DelegatedFromId { get; set; }

    public int AgingDays => (DateTime.UtcNow - CreatedAt).Days;
}

public enum WorkItemType
{
    [Display(Name = "اعتماد")]
    Approval = 0,
    [Display(Name = "مهمة")]
    Task = 1,
    [Display(Name = "مراجعة")]
    Review = 2,
    [Display(Name = "إقرار")]
    Acknowledgement = 3,
    [Display(Name = "معلومات")]
    Information = 4
}

public enum WorkItemPriority
{
    [Display(Name = "منخفضة")]
    Low = 0,
    [Display(Name = "عادية")]
    Normal = 1,
    [Display(Name = "عالية")]
    High = 2,
    [Display(Name = "عاجلة")]
    Urgent = 3
}

public enum WorkItemStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending = 0,
    [Display(Name = "قيد التنفيذ")]
    InProgress = 1,
    [Display(Name = "مكتمل")]
    Completed = 2,
    [Display(Name = "مُفوَّض")]
    Delegated = 3,
    [Display(Name = "مُصعَّد")]
    Escalated = 4,
    [Display(Name = "ملغى")]
    Cancelled = 5
}
