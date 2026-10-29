using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-031 (PLAN-31): exam session type (stored as string).</summary>
public enum ExamSessionType
{
    [Display(Name = "كتابي")]
    Written = 0,
    [Display(Name = "عملي")]
    Practical = 1,
    [Display(Name = "شفهي")]
    Oral = 2
}

/// <summary>HCM-031: exam session status (stored as string).</summary>
public enum ExamSessionStatus
{
    [Display(Name = "مخططة")]
    Planned = 0,
    [Display(Name = "تم التوزيع")]
    Seated = 1,
    [Display(Name = "عُقدت")]
    Held = 2,
    [Display(Name = "أُغلق التصحيح")]
    MarkingClosed = 3
}

/// <summary>HCM-031: attendance of an exam candidate (stored as string).</summary>
public enum ExamAttendance
{
    [Display(Name = "غير مسجل")]
    Unknown = 0,
    [Display(Name = "حاضر")]
    Present = 1,
    [Display(Name = "غائب")]
    Absent = 2
}

/// <summary>HCM-031: role of a marker on a session (stored as string).</summary>
public enum ExamMarkerRole
{
    [Display(Name = "المصحح الأول")]
    First = 0,
    [Display(Name = "المصحح الثاني")]
    Second = 1,
    [Display(Name = "المصحح الثالث")]
    Reconciler = 2
}

/// <summary>HCM-031: marking round of one score (stored as string).</summary>
public enum ExamScoreRound
{
    [Display(Name = "التصحيح الأول")]
    First = 0,
    [Display(Name = "التصحيح الثاني")]
    Second = 1,
    [Display(Name = "التسوية")]
    Reconciler = 2,
    /// <summary>Entered by the independent reviewer when a result objection is upheld (P4).</summary>
    [Display(Name = "تصحيح بعد اعتراض")]
    ObjectionCorrection = 3
}
