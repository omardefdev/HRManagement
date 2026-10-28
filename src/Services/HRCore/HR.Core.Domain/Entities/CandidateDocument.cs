using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>
/// HCM-030 (E3, G-17): a document a candidate attached to a recruitment application. The bytes live in File.Storage
/// (uploaded through HR.Core's service identity, quarantined and scanned there); this row keeps the file id and the
/// display data. The file reference (<c>hr-core:CandidateDocument:{Id:N}:FileId</c>) is registered in the same save.
/// </summary>
public class CandidateDocument : BaseEntity
{
    [Display(Name = "طلب التوظيف")]
    public Guid RecruitmentApplicationId { get; set; }

    /// <summary>The public channel row the document came with (null for HR-added documents).</summary>
    [Display(Name = "طلب البوابة")]
    public Guid? CitizenApplicationId { get; set; }

    /// <summary>One of <see cref="Services.CandidateDocumentTypes"/>.</summary>
    [Required, StringLength(20)]
    [Display(Name = "نوع المستند")]
    public string DocumentType { get; set; } = string.Empty;

    [Display(Name = "معرّف الملف")]
    public Guid FileId { get; set; }

    /// <summary>Sanitized with <c>FileUploadHelper.SanitizeFileName</c>; may carry the candidate's name, so not audited.</summary>
    [Required, StringLength(256)]
    [Display(Name = "اسم الملف")]
    [AuditRedact]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Content type detected from the file's first bytes at upload (File.Storage may change it after the scan).</summary>
    [Required, StringLength(100)]
    [Display(Name = "نوع المحتوى")]
    public string ContentType { get; set; } = string.Empty;

    [Display(Name = "الحجم (بايت)")]
    public long SizeBytes { get; set; }

    [Display(Name = "وقت الرفع")]
    public DateTime UploadedAt { get; set; }

    /// <summary><see cref="UploadedByCandidate"/> or the HR user id.</summary>
    [Required, StringLength(64)]
    [Display(Name = "رفعه")]
    public string UploadedBy { get; set; } = UploadedByCandidate;

    public const string UploadedByCandidate = "Candidate";
}
