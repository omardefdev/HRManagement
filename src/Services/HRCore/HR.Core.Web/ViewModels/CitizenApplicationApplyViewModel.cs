using System.ComponentModel.DataAnnotations;

namespace HR.Core.Web.ViewModels;

public class CitizenApplicationApplyViewModel
{
    [Display(Name = "الإعلان الوظيفي")]
    public Guid PublicJobPostingId { get; set; }

    [Required(ErrorMessage = "الاسم الكامل مطلوب")]
    [Display(Name = "الاسم الكامل")]
    [StringLength(300)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "الرقم الوطني مطلوب")]
    [Display(Name = "الرقم الوطني")]
    [StringLength(20)]
    public string NationalId { get; set; } = string.Empty;

    [Display(Name = "رقم الهاتف")]
    [StringLength(20)]
    public string? Phone { get; set; }

    [Display(Name = "البريد الإلكتروني")]
    [StringLength(150)]
    public string? Email { get; set; }

    [Display(Name = "نبذة/مسوّغات")]
    public string? CoverNote { get; set; }

    [Display(Name = "موافقة معالجة البيانات")]
    [Range(typeof(bool), "true", "true", ErrorMessage = "يجب الموافقة على معالجة البيانات.")]
    public bool PrivacyConsent { get; set; }

    [Display(Name = "حقل أمان")]
    public string? Website { get; set; }
}
