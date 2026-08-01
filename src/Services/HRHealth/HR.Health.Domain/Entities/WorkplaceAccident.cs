using System.ComponentModel.DataAnnotations;
using HR.Health.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "حادث عمل")]
public class WorkplaceAccident : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "اسم الموظف")]
    [StringLength(300)]
    public string? EmployeeName { get; set; }

    [Required]
    [Display(Name = "تاريخ الحادث")]
    public DateOnly AccidentDate { get; set; }

    [Required(ErrorMessage = "الموقع مطلوب")]
    [Display(Name = "موقع الحادث")]
    [StringLength(200)]
    public string Location { get; set; } = string.Empty;

    [Required(ErrorMessage = "وصف الحادث مطلوب")]
    [Display(Name = "وصف الحادث")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "درجة الخطورة")]
    public AccidentSeverity Severity { get; set; } = AccidentSeverity.Minor;

    [Display(Name = "أيام الإجازة المرضية الناتجة")]
    [Range(0, 365)]
    public int RelatedSickLeaveDays { get; set; }

    [Display(Name = "إجراءات التحقيق والوقاية")]
    public string? InvestigationNotes { get; set; }

    [Display(Name = "السجل الطبي المرتبط")]
    public Guid? MedicalRecordId { get; set; }
}
