using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Domain.Entities;

[Display(Name = "سجل إعطاء العهدة")]
public class AssetAssignment : AuditableEntity
{
    [Required]
    [Display(Name = "الأصل")]
    public Guid AssetId { get; set; }

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "تاريخ التسليم")]
    public DateTime AssignedAt { get; set; }

    [Display(Name = "تاريخ الإعادة المتوقع")]
    public DateTime? DueBackDate { get; set; }

    [Display(Name = "الحالة عند التسليم")]
    public string? ConditionOnAssign { get; set; }

    [Display(Name = "تاريخ الإعادة")]
    public DateTime? ReturnedAt { get; set; }

    [MaxLength(256)]
    public string? EvidenceRef { get; set; }

    public Asset Asset { get; set; } = default!;
}
