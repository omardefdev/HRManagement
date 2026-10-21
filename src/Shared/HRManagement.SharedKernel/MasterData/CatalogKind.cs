using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.MasterData;

/// <summary>MDM-001: discriminator for <see cref="ReferenceCatalogEntry"/> rows.</summary>
public enum CatalogKind
{
    [Display(Name = "تصنيف وظيفي")]
    JobClassification = 1,
    [Display(Name = "درجة")]
    Grade = 2,
    [Display(Name = "جغرافيا")]
    Geography = 3,
    [Display(Name = "جهة")]
    Agency = 4,
    [Display(Name = "مؤهل")]
    Qualification = 5,
    [Display(Name = "مصرف")]
    Bank = 6,
    [Display(Name = "عملة")]
    Currency = 7,
    [Display(Name = "مكوّن راتب")]
    SalaryComponent = 8,
    [Display(Name = "نوع مستند")]
    DocumentType = 9,
    [Display(Name = "نوع عقد")]
    ContractType = 10,
    [Display(Name = "نوع حركة")]
    MovementType = 11,
    [Display(Name = "نوع معاملة")]
    ServiceType = 12
}
