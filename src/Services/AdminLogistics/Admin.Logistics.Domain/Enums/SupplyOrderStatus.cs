using System.ComponentModel.DataAnnotations;

namespace Admin.Logistics.Domain.Enums;

public enum SupplyOrderStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "مُنفَّذ")]
    Fulfilled,
    [Display(Name = "ملغى")]
    Cancelled,
    [Display(Name = "مغلق")]
    Closed,
}
