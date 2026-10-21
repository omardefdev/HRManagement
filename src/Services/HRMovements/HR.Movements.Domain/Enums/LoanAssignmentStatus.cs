using System.ComponentModel.DataAnnotations;

namespace HR.Movements.Domain.Enums;

public enum LoanAssignmentStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "نشط")]
    Active,
    [Display(Name = "مغلق")]
    Closed,
}