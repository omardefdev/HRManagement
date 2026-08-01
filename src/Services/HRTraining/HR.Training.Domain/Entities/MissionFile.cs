using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HR.Training.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "ملف البعثة")]
public class MissionFile : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required(ErrorMessage = "الوجهة مطلوبة")]
    [Display(Name = "الوجهة")]
    public string Destination { get; set; } = default!;

    [Required]
    [Display(Name = "تاريخ البداية")]
    public DateOnly StartDate { get; set; }

    [Display(Name = "تاريخ النهاية")]
    public DateOnly? EndDate { get; set; }

    [Display(Name = "الغرض")]
    public string? Purpose { get; set; }

    [Display(Name = "الجهة الراعية")]
    public string? Sponsor { get; set; }

    [Display(Name = "الحالة")]
    public MissionStatus Status { get; set; }

    [Display(Name = "المستندات")]
    [Column(TypeName = "jsonb")]
    public string? Documents { get; set; }
}
