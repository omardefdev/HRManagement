using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Records.Archive.Domain.Enums;

namespace Records.Archive.Domain.Entities;

[Display(Name = "عنصر الأرشيف")]
public class ArchiveItem : AuditableEntity
{
    [Required(ErrorMessage = "العنوان مطلوب")]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = default!;

    [Required(ErrorMessage = "الرقم المرجعي مطلوب")]
    [Display(Name = "الرقم المرجعي")]
    public string ReferenceNumber { get; set; } = default!;

    [Display(Name = "التصنيف")]
    public Guid? ClassificationId { get; set; }

    [Display(Name = "مكان التخزين")]
    public string? StorageLocation { get; set; }

    [Display(Name = "النوع")]
    public ArchiveFormat Format { get; set; }

    [Display(Name = "المنشئ")]
    public Guid? Creator { get; set; }

    [Display(Name = "فترة الاحتفاظ بالسنوات")]
    public int? RetentionPeriodYears { get; set; }

    /// <summary>FUN-006: legal hold blocks disposal/purge.</summary>
    public bool IsOnLegalHold { get; set; }
    public DateTime? LegalHoldAt { get; set; }

    [MaxLength(500)]
    public string? LegalHoldReason { get; set; }

    public Guid? DisposeRequestedBy { get; set; }
    public DateTime? DisposeRequestedAt { get; set; }
    public Guid? DisposeApprovedBy { get; set; }
    public DateTime? DisposeApprovedAt { get; set; }
    public bool IsDisposeCancelled { get; set; }

    [MaxLength(500)]
    public string? DisposeCancelReason { get; set; }

    [MaxLength(512)]
    public string? CompletenessAttestation { get; set; }

    public DocumentClassification? Classification { get; set; }
    public ICollection<PhysicalFileRecord> PhysicalFiles { get; set; } = new List<PhysicalFileRecord>();
    public ICollection<DigitalFileRecord> DigitalFiles { get; set; } = new List<DigitalFileRecord>();
    public ICollection<LoanRecord> LoanRecords { get; set; } = new List<LoanRecord>();
    public ICollection<ArchiveIndexEntry> IndexEntries { get; set; } = new List<ArchiveIndexEntry>();
    public ICollection<CustodyEvent> CustodyEvents { get; set; } = new List<CustodyEvent>();
}
