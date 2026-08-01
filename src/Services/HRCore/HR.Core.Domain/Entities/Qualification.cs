using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class Qualification : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "كود التأهيل المهني")]
    [StringLength(50)]
    public string? QualificationCode { get; set; }

    [Display(Name = "اسم المؤهل")]
    [Required(ErrorMessage = "اسم المؤهل مطلوب")]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الجهة المانحة")]
    [StringLength(300)]
    public string? Provider { get; set; }

    [Display(Name = "تاريخ الإتمام")]
    [DataType(DataType.Date)]
    public DateTime? CompletionDate { get; set; }

    [Display(Name = "معرف ملف الشهادة")]
    public Guid? FileId { get; set; }

    public virtual Employee? Employee { get; set; }
}
