using System.ComponentModel.DataAnnotations;

namespace IT.Infrastructure.Domain.Enums;

public enum ServerStatus
{
    [Display(Name = "نشط")]
    Active,
    [Display(Name = "مُخرَج من الخدمة")]
    Decommissioned,
}