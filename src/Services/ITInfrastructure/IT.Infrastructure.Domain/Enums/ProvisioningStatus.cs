using System.ComponentModel.DataAnnotations;

namespace IT.Infrastructure.Domain.Enums;

/// <summary>FUN-005: directory/account provisioning outcome on hire tickets.</summary>
public enum ProvisioningStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending = 0,
    [Display(Name = "ناجح")]
    Succeeded = 1,
    [Display(Name = "فشل")]
    Failed = 2
}
