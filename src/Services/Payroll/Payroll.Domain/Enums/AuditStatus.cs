using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum AuditStatus
{
    [Display(Name = "مفتوح")]
    Open,
    [Display(Name = "مغلق")]
    Closed,
}