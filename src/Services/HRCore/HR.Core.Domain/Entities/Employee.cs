using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Entities;

public class Employee : AuditableEntity
{
    [Display(Name = "الرقم الوطني")]
    [Required(ErrorMessage = "الرقم الوطني مطلوب")]
    [StringLength(20)]
    [AuditRedact]
    public string NationalId { get; set; } = string.Empty;

    /// <summary>SHA-256 of plaintext NationalId for uniqueness/search while column is encrypted at rest.</summary>
    [MaxLength(64)]
    public string? NationalIdHash { get; set; }

    [Display(Name = "الاسم الأول")]
    [Required(ErrorMessage = "الاسم الأول مطلوب")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Display(Name = "الاسم الأوسط")]
    [StringLength(100)]
    public string? MiddleName { get; set; }

    [Display(Name = "اسم العائلة")]
    [Required(ErrorMessage = "اسم العائلة مطلوب")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Display(Name = "الاسم الكامل")]
    [StringLength(300)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FirstNameNormalized { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? MiddleNameNormalized { get; set; }

    [MaxLength(100)]
    public string LastNameNormalized { get; set; } = string.Empty;

    [MaxLength(300)]
    public string FullNameNormalized { get; set; } = string.Empty;

    public int NormalizationVersion { get; set; }

    [Display(Name = "تاريخ الميلاد")]
    [Required(ErrorMessage = "تاريخ الميلاد مطلوب")]
    [DataType(DataType.Date)]
    public DateTime BirthDate { get; set; }

    [Display(Name = "الجنس")]
    [Required(ErrorMessage = "الجنس مطلوب")]
    public Gender Gender { get; set; }

    [Display(Name = "معرف ملف الصورة")]
    public Guid? PhotoFileId { get; set; }

    [Display(Name = "الحالة الاجتماعية")]
    public MaritalStatus MaritalStatus { get; set; }

    [Display(Name = "معلومات الاتصال الأساسية")]
    public Guid? PrimaryContactId { get; set; }

    [Display(Name = "الحالة الوظيفية")]
    public EmploymentStatus CurrentStatus { get; set; } = EmploymentStatus.Active;

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? PrimaryOrgUnitId { get; set; }

    [Display(Name = "تاريخ التعيين")]
    [DataType(DataType.Date)]
    public DateTime? HireDate { get; set; }

    [Display(Name = "الشريحة الوظيفية")]
    public EmployeeCadre Cadre { get; set; } = EmployeeCadre.Executive;

    [Display(Name = "نمط الدوام")]
    public HR.Core.Domain.Enums.WorkScheduleMode WorkScheduleMode { get; set; } = HR.Core.Domain.Enums.WorkScheduleMode.FullTime;

    /// <summary>DATA-002: blocks anonymize/merge/purge while true.</summary>
    [Display(Name = "حجز قانوني")]
    public bool LegalHold { get; set; }

    [Display(Name = "سبب الحجز القانوني")]
    [StringLength(500)]
    public string? LegalHoldReason { get; set; }

    public DateTime? LegalHoldSetAt { get; set; }

    public Guid? LegalHoldSetBy { get; set; }

    /// <summary>DATA-002: when CurrentStatus is Merged, points at surviving employee.</summary>
    public Guid? MergedIntoEmployeeId { get; set; }

    public virtual ICollection<EmployeeProfile> Profiles { get; set; } = new List<EmployeeProfile>();
    public virtual ICollection<EmploymentRecord> EmploymentRecords { get; set; } = new List<EmploymentRecord>();
    public virtual ICollection<EmployeeDocument> Documents { get; set; } = new List<EmployeeDocument>();
    public virtual ICollection<Qualification> Qualifications { get; set; } = new List<Qualification>();
    public virtual ICollection<ContactInfo> Contacts { get; set; } = new List<ContactInfo>();
    public virtual ICollection<EmploymentStatusHistory> StatusHistory { get; set; } = new List<EmploymentStatusHistory>();
    public virtual ICollection<PerformanceEvaluation> Evaluations { get; set; } = new List<PerformanceEvaluation>();
    public virtual ICollection<EmployeeCorrection> Corrections { get; set; } = new List<EmployeeCorrection>();
    public virtual OrgUnit? PrimaryOrgUnit { get; set; }
}
