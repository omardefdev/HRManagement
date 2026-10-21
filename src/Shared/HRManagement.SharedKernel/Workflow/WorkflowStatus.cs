using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Workflow;

public enum WorkflowStatus
{
    [Display(Name = "لم يبدأ")]
    NotStarted,
    [Display(Name = "قيد التنفيذ")]
    InProgress,
    [Display(Name = "مكتمل")]
    Completed,
    [Display(Name = "مرفوض")]
    Rejected,
    [Display(Name = "ملغى")]
    Cancelled
}
