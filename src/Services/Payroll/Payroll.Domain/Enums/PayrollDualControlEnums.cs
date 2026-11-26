using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

/// <summary>PLAN-58 (§3, §7): the acts that need two people. Stored as an integer (a new table, PLAN-57's rule).</summary>
public enum PayrollDualControlKind
{
    [Display(Name = "الإفراج عن حجز صرف")]
    HoldRelease = 0,

    [Display(Name = "حل تنبيه احتيال حرج")]
    CriticalAlertResolve = 1,

    [Display(Name = "تعديل قيد متأخرات")]
    ArrearsAdjust = 2,

    [Display(Name = "إعادة إصدار ملف مصرفي")]
    BankFileReissue = 3,

    [Display(Name = "تسجيل تسوية دفعة")]
    BatchSettle = 4,

    [Display(Name = "تسجيل رفض القناة لدفعة")]
    BatchReject = 5,
}

/// <summary>PLAN-58 (§3): where a four-eyes request stands. Stored as an integer.</summary>
public enum PayrollDualControlStatus
{
    [Display(Name = "بانتظار القرار")]
    Pending = 0,

    [Display(Name = "معتمد")]
    Approved = 1,

    [Display(Name = "مرفوض")]
    Rejected = 2,

    [Display(Name = "منتهي المهلة")]
    Expired = 3,
}
