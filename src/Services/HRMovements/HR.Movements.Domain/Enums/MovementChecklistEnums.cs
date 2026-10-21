using System.ComponentModel.DataAnnotations;

namespace HR.Movements.Domain.Enums;

public enum MovementKind
{
    [Display(Name = "نقل")]
    Transfer = 0,
    [Display(Name = "إعارة")]
    Secondment = 1,
    [Display(Name = "إعارة")]
    Loan = 2,
    [Display(Name = "ترفيع")]
    Promotion = 3,
    [Display(Name = "ندب")]
    Delegation = 4
}

public enum MovementChecklistStep
{
    [Display(Name = "تسجيل الطلب")]
    RequestRecorded = 0,
    [Display(Name = "معتمد")]
    Approved = 1,
    [Display(Name = "مراجعة مالية")]
    FinancialReviewed = 2,
    [Display(Name = "مزامنة تنظيمية")]
    OrgSynced = 3,
    [Display(Name = "تحديث الدليل")]
    DirectoryUpdated = 4,
    [Display(Name = "تسجيل العهدة")]
    AssetsNoted = 5,
    [Display(Name = "منفّذ")]
    Executed = 6,
    [Display(Name = "منتهي")]
    Ended = 7
}
