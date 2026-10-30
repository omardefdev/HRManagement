using System.ComponentModel.DataAnnotations;

namespace IDCard.Domain.Enums;

public enum CardRequestStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending,
    [Display(Name = "معالج")]
    Processed,
    [Display(Name = "مرفوض")]
    Rejected,
}
