using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Entities;

public class ContactInfo : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع الاتصال")]
    public ContactType Type { get; set; }

    [Display(Name = "القيمة")]
    [Required(ErrorMessage = "قيمة الاتصال مطلوبة")]
    public string Value { get; set; } = string.Empty;

    [Display(Name = "أساسي")]
    public bool IsPrimary { get; set; }

    public virtual Employee? Employee { get; set; }
}
