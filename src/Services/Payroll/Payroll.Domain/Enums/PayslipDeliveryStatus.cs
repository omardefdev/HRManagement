using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum PayslipDeliveryStatus
{
    [Display(Name = "مُولَّد")]
    Generated = 0,
    [Display(Name = "مُرسَل")]
    Sent = 1,
    [Display(Name = "مُشاهَد")]
    Viewed = 2,
}
