using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Workflow;

public enum WorkflowStepType
{
    [Display(Name = "اعتماد")]
    Approval,
    [Display(Name = "مراجعة")]
    Review,
    [Display(Name = "إشعار")]
    Notification,
    [Display(Name = "شرط")]
    Condition
}
