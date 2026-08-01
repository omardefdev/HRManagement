using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Web.ViewModels;

public class ContractTypeCreateViewModel
{
    [Display(Name = "الرمز")]
    [Required(ErrorMessage = "الرمز مطلوب")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "الاسم بالعربي")]
    [Required(ErrorMessage = "الاسم بالعربي مطلوب")]
    [StringLength(200)]
    public string NameAr { get; set; } = string.Empty;

    [Display(Name = "الاسم بالإنكليزي")]
    [StringLength(200)]
    public string? NameEn { get; set; }

    [Display(Name = "الوصف")]
    public string? Description { get; set; }
}

public class ContractTypeEditViewModel : ContractTypeCreateViewModel
{
    public Guid Id { get; set; }
}
