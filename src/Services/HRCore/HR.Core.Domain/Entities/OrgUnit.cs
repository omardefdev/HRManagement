using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Localization;

namespace HR.Core.Domain.Entities;

public class OrgUnit : AuditableEntity, ILocalizedName
{
    [Display(Name = "رمز الوحدة")]
    [Required(ErrorMessage = "رمز الوحدة مطلوب")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "الاسم بالعربي")]
    [Required(ErrorMessage = "الاسم بالعربي مطلوب")]
    [StringLength(200)]
    public string NameAr { get; set; } = string.Empty;

    [Display(Name = "الاسم بالإنكليزي")]
    [StringLength(200)]
    public string? NameEn { get; set; }

    [Display(Name = "الوحدة الأم")]
    public Guid? ParentId { get; set; }

    [Display(Name = "رئيس الوحدة")]
    public Guid? HeadEmployeeId { get; set; }

    [Display(Name = "المسار الهرمي")]
    [StringLength(2000)]
    public string MaterializedPath { get; set; } = string.Empty;

    public virtual OrgUnit? Parent { get; set; }
    public virtual ICollection<OrgUnit> Children { get; set; } = new List<OrgUnit>();
    public virtual Employee? HeadEmployee { get; set; }
}
