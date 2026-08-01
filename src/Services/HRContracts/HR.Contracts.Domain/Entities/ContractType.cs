using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "نوع العقد")]
public class ContractType : AuditableEntity
{
    [Display(Name = "الرمز")]
    [Required(ErrorMessage = "الرمز مطلوب")]
    public string Code { get; set; } = null!;

    [Display(Name = "الاسم بالعربي")]
    [Required(ErrorMessage = "الاسم بالعربي مطلوب")]
    public string NameAr { get; set; } = null!;

    [Display(Name = "الاسم بالإنكليزي")]
    public string? NameEn { get; set; }

    [Display(Name = "الوصف")]
    public string? Description { get; set; }
}
