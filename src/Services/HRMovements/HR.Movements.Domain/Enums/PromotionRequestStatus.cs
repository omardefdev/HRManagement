using System.ComponentModel.DataAnnotations;

namespace HR.Movements.Domain.Enums;

public enum PromotionRequestStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "منفّذ")]
    Executed,
    [Display(Name = "ملغى")]
    Cancelled
}
