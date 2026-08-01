using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.ViewModels;
using HR.Core.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Core.Web.ViewModels;

public class OrgUnitListViewModel
{
    public PaginatedList<OrgUnit> OrgUnits { get; set; } = null!;
    public SearchFilterBase Filter { get; set; } = new();
}

public class OrgUnitCreateViewModel
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

    public SelectList? ParentUnits { get; set; }
    public SelectList? Employees { get; set; }
}

public class OrgUnitEditViewModel : OrgUnitCreateViewModel
{
    public Guid Id { get; set; }
}

public class OrgUnitTreeViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? HeadEmployeeName { get; set; }
    public List<OrgUnitTreeViewModel> Children { get; set; } = [];
}
