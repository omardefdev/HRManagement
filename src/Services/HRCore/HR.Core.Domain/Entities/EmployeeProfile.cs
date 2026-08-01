using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class EmployeeProfile : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "الجنسية")]
    [StringLength(100)]
    public string? Nationality { get; set; }

    [Display(Name = "مكان الميلاد")]
    [StringLength(200)]
    public string? PlaceOfBirth { get; set; }

    [Display(Name = "جهة إصدار الرقم الوطني")]
    [StringLength(200)]
    public string? NationalIdIssuePlace { get; set; }

    [Display(Name = "عنوان السكن")]
    public string? ResidentialAddress { get; set; }

    [Display(Name = "العنوان الدائم")]
    public string? PermanentAddress { get; set; }

    [Display(Name = "جهة اتصال طارئة")]
    public string? EmergencyContact { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public virtual Employee? Employee { get; set; }
}
