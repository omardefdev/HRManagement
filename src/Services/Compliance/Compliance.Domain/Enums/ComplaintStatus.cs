using System.ComponentModel.DataAnnotations;

namespace Compliance.Domain.Enums;

public enum ComplaintStatus
{
    [Display(Name = "مفتوح")]
    Open,
    [Display(Name = "قيد التحقيق")]
    Investigating,
    [Display(Name = "محلول")]
    Resolved,
    [Display(Name = "مغلق")]
    Closed,
}
