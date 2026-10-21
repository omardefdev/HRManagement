using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum EosStatus
{
    [Display(Name = "مسودة")]
    Draft,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "مدفوع")]
    Paid,
    /// <summary>FUN-002: draft voided by separation compensate.</summary>
    [Display(Name = "ملغى")]
    Cancelled
}
