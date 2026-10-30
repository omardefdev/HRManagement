using System.ComponentModel.DataAnnotations;

namespace Admin.Logistics.Domain.Enums;

public enum MaintenanceType
{
    [Display(Name = "وقائي")]
    Preventive,
    [Display(Name = "تصحيحي")]
    Corrective,
}
