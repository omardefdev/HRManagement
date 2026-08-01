using System.ComponentModel.DataAnnotations;
using File.Storage.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace File.Storage.Domain.Entities;

[Display(Name = "ملف")]
public class FileRecord : AuditableEntity
{
    [Display(Name = "اسم الملف الأصلي")]
    [StringLength(256)]
    public string OriginalName { get; set; } = default!;

    [Display(Name = "الامتداد")]
    [StringLength(32)]
    public string Extension { get; set; } = default!;

    [Display(Name = "نوع المحتوى")]
    [StringLength(128)]
    public string MimeType { get; set; } = "application/octet-stream";

    [Display(Name = "الحجم")]
    public long Size { get; set; }

    [Display(Name = "SHA256")]
    [StringLength(64)]
    public string? Sha256 { get; set; }

    [Display(Name = "الحاوية")]
    [Required]
    [StringLength(128)]
    public string Bucket { get; set; } = default!;

    [Display(Name = "المفتاح")]
    [Required]
    [StringLength(512)]
    public string ObjectKey { get; set; } = default!;

    [Display(Name = "رفع بواسطة")]
    public Guid? UploadedBy { get; set; }

    [Display(Name = "تاريخ الرفع")]
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "الحالة")]
    [StringLength(32)]
    public string Status { get; set; } = FileRecordStatus.Pending;

    [Display(Name = "الرؤية")]
    [StringLength(32)]
    public string Visibility { get; set; } = FileVisibility.Private;

    [Display(Name = "محذوف")]
    public bool IsDeleted { get; set; }

    [Display(Name = "تاريخ الحذف")]
    public DateTime? DeletedAt { get; set; }

    [Display(Name = "عدد المراجع")]
    public int ReferenceCount { get; set; }

    [Display(Name = "حجز قانوني")]
    public bool LegalHold { get; set; }

    [Display(Name = "الاحتفاظ حتى")]
    public DateTime? RetainUntil { get; set; }

    [Display(Name = "سبب الحذف القسري")]
    [StringLength(500)]
    public string? ForceDeleteReason { get; set; }

    [Display(Name = "تاريخ حذف الكائن")]
    public DateTime? ObjectPurgedAt { get; set; }

    [Display(Name = "حالة الفحص")]
    [StringLength(32)]
    public string ScanStatus { get; set; } = FileScanStatus.PendingScan;

    [Display(Name = "نوع المحتوى المكتشف")]
    [StringLength(128)]
    public string? DetectedContentType { get; set; }

    [Display(Name = "تاريخ الفحص")]
    public DateTime? ScannedAt { get; set; }

    [Display(Name = "اسم الماسح")]
    [StringLength(64)]
    public string? ScannerName { get; set; }

    [Display(Name = "إصدار الماسح")]
    [StringLength(64)]
    public string? ScannerVersion { get; set; }

    [Display(Name = "التهديد")]
    [StringLength(256)]
    public string? ThreatName { get; set; }

    [Display(Name = "طُبّق CDR")]
    public bool CdrApplied { get; set; }

    [Display(Name = "ملف CDR")]
    [StringLength(64)]
    public string? CdrProfile { get; set; }

    public ICollection<FileReferenceRecord> References { get; set; } = new List<FileReferenceRecord>();
}
