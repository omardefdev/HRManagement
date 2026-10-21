using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

public enum ContractStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending,
    [Display(Name = "نشط")]
    Active,
    [Display(Name = "منتهي")]
    Expired,
    [Display(Name = "منتهي")]
    Terminated
}
