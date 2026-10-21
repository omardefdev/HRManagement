using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Enums;

public enum EmploymentStatus
{
    [Display(Name = "نشط")]
    Active,
    [Display(Name = "موقوف")]
    Suspended,
    [Display(Name = "منتهي")]
    Terminated,
    [Display(Name = "متقاعد")]
    Retired,
    [Display(Name = "في إجازة")]
    OnLeave,
    [Display(Name = "معار")]
    Seconded,
    [Display(Name = "مستقيل")]
    Resigned,
    /// <summary>DATA-002: end of life while employed or after separation recording.</summary>
    [Display(Name = "متوفى")]
    Deceased,
    /// <summary>DATA-002: duplicate record closed; see MergedIntoEmployeeId.</summary>
    [Display(Name = "مدمج")]
    Merged,
    /// <summary>DATA-002: PII scrubbed under approved retention policy (not hard delete).</summary>
    [Display(Name = "مجهول الهوية")]
    Anonymized,
    /// <summary>Recruitment has created the employee record, but the appointment checklist is not complete yet.</summary>
    [Display(Name = "بانتظار استكمال التعيين")]
    PendingActivation
}
