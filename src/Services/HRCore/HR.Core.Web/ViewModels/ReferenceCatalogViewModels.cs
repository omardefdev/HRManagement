using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.MasterData;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.ViewModels;

public sealed class ReferenceCatalogIndexViewModel : SearchFilterBase
{
    public CatalogKind Kind { get; set; } = CatalogKind.Grade;
    public bool? IsActive { get; set; }
    public IReadOnlyList<ReferenceCatalogListItem> Items { get; set; } = [];
    public bool CanWrite { get; set; }

    public string KindLabel => Kind switch
    {
        CatalogKind.Grade => "الدرجات",
        CatalogKind.JobClassification => "التصنيفات الوظيفية",
        _ => Kind.ToString()
    };
}

public class ReferenceCatalogFormViewModel
{
    public CatalogKind Kind { get; set; } = CatalogKind.Grade;

    [Required(ErrorMessage = "الرمز مطلوب")]
    [StringLength(50)]
    [Display(Name = "الرمز")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "الاسم بالعربية مطلوب")]
    [StringLength(300)]
    [Display(Name = "الاسم بالعربية")]
    public string NameAr { get; set; } = string.Empty;

    [StringLength(300)]
    [Display(Name = "الاسم بالإنجليزية")]
    public string? NameEn { get; set; }

    [Display(Name = "صالح من")]
    [DataType(DataType.Date)]
    public DateTime? ValidFrom { get; set; }

    [Display(Name = "صالح حتى")]
    [DataType(DataType.Date)]
    public DateTime? ValidTo { get; set; }

    public string KindLabel => Kind switch
    {
        CatalogKind.Grade => "الدرجة",
        CatalogKind.JobClassification => "التصنيف الوظيفي",
        _ => Kind.ToString()
    };
}

public sealed class ReferenceCatalogEditViewModel : ReferenceCatalogFormViewModel
{
    public Guid Id { get; set; }
    public bool IsReadOnly { get; set; }
}

public sealed class ReferenceCatalogDetailsViewModel
{
    public ReferenceCatalogEntry Entry { get; set; } = null!;
    public bool IsPlatformOwned { get; set; }
    public bool CanMutate { get; set; }
    public IReadOnlyList<ReferenceImpactAnalyzer.ImpactResult> Impact { get; set; } = [];
}
