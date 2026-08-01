using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "الوردية")]
public class Shift : AuditableEntity
{
    [Required(ErrorMessage = "رمز الوردية مطلوب")]
    [Display(Name = "رمز الوردية")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم الوردية مطلوب")]
    [Display(Name = "اسم الوردية")]
    [StringLength(150)]
    public string NameAr { get; set; } = string.Empty;

    [Display(Name = "نوع الوردية")]
    public ShiftType ShiftType { get; set; } = ShiftType.Day;

    [Display(Name = "وقت البدء")]
    public TimeOnly StartTime { get; set; } = new(8, 0);

    [Display(Name = "وقت الانتهاء")]
    public TimeOnly EndTime { get; set; } = new(16, 0);

    [Display(Name = "مفعّلة")]
    public bool IsActive { get; set; } = true;

    public virtual ICollection<ShiftAssignment> Assignments { get; set; } = new List<ShiftAssignment>();
}
