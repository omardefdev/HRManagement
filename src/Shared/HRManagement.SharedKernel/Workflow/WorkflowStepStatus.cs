using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Workflow;

public enum WorkflowStepStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "مرفوض")]
    Rejected,
    [Display(Name = "متخطى")]
    Skipped
}
