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

    // HCM-030 (D1): application profile (optional; missing data is flagged, never refused).

    [Display(Name = "المؤهل العلمي")]
    [StringLength(20)]
    public string? EducationLevelCode { get; set; }

    [Display(Name = "التخصص")]
    [StringLength(200, ErrorMessage = "التخصص يجب ألا يتجاوز 200 حرف.")]
    public string? Specialization { get; set; }

    [Display(Name = "سنة التخرج")]
    [Range(MinGraduationYear, 2100, ErrorMessage = "سنة التخرج غير صالحة.")]
    public int? GraduationYear { get; set; }

    [Display(Name = "سنوات الخبرة")]
    [Range(typeof(decimal), "0", "60", ErrorMessage = "سنوات الخبرة يجب أن تكون بين 0 و60.")]
    public decimal? YearsOfExperience { get; set; }

    [Display(Name = "تاريخ الميلاد")]
    [DataType(DataType.Date)]
    public DateOnly? BirthDate { get; set; }

    public const int MinGraduationYear = 1950;
    public const int MinApplicantAge = 14;
    public const int MaxApplicantAge = 100;

    [Display(Name = "موافقة معالجة البيانات")]
    [Range(typeof(bool), "true", "true", ErrorMessage = "يجب الموافقة على معالجة البيانات.")]
    public bool PrivacyConsent { get; set; }

    [Display(Name = "حقل أمان")]
    public string? Website { get; set; }
}
