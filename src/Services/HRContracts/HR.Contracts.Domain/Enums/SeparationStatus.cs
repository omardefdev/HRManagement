using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

/// <summary>
/// PLAN-60 (G-SEP-04): the separation record's own state, kept on the server and changed only through
/// <see cref="Services.SeparationWorkflowRules.Transition"/>. Stored as text.
/// </summary>
public enum SeparationStatus
{
    [Display(Name = "مسودة")]
    Draft,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "مرفوض")]
    Rejected,
    [Display(Name = "قيد التنفيذ")]
    Executing,
    [Display(Name = "بانتظار تدخل")]
    AwaitingIntervention,
    [Display(Name = "مكتمل")]
    Completed,
    [Display(Name = "ملغى")]
    Cancelled
}
