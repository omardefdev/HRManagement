using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Services;

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

    // HCM-030 (E5): attachments (requisition-linked postings only). One input per type; a missing required type is an
    // eligibility flag, never a refusal.

    [Display(Name = "السيرة الذاتية")]
    public IFormFile? CvFile { get; set; }

    [Display(Name = "الشهادات")]
    public List<IFormFile>? CertificateFiles { get; set; }

    [Display(Name = "صورة الهوية")]
    public IFormFile? IdCopyFile { get; set; }

    [Display(Name = "مستندات أخرى")]
    public List<IFormFile>? OtherFiles { get; set; }

    /// <summary>Attached files with their document type and form field (empty inputs are not bound).</summary>
    public IReadOnlyList<(string DocumentType, IFormFile File, string FieldName)> AttachedFiles()
    {
        var files = new List<(string, IFormFile, string)>();
        if (CvFile is not null)
            files.Add((CandidateDocumentTypes.Cv, CvFile, nameof(CvFile)));
        files.AddRange((CertificateFiles ?? []).Where(f => f is not null)
            .Select(f => (CandidateDocumentTypes.Certificate, f, nameof(CertificateFiles))));
        if (IdCopyFile is not null)
            files.Add((CandidateDocumentTypes.IdCopy, IdCopyFile, nameof(IdCopyFile)));
        files.AddRange((OtherFiles ?? []).Where(f => f is not null)
            .Select(f => (CandidateDocumentTypes.Other, f, nameof(OtherFiles))));
        return files;
    }

    /// <summary>Form field of each document type's input (shared by the view and the controller's errors).</summary>
    public static string FieldFor(string documentType) => documentType switch
    {
        CandidateDocumentTypes.Cv => nameof(CvFile),
        CandidateDocumentTypes.Certificate => nameof(CertificateFiles),
        CandidateDocumentTypes.IdCopy => nameof(IdCopyFile),
        _ => nameof(OtherFiles)
    };
}
