using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

public enum ServiceTransactionState
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "مُرسَل")]
    Submitted = 1,
    [Display(Name = "قيد المراجعة")]
    UnderReview = 2,
    [Display(Name = "بحاجة لمعلومات")]
    NeedsInfo = 3,
    [Display(Name = "قيد المعالجة")]
    InProcess = 4,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval = 5,
    [Display(Name = "معتمد")]
    Approved = 6,
    [Display(Name = "مرفوض")]
    Rejected = 7,
    [Display(Name = "قيد التنفيذ")]
    Executing = 8,
    [Display(Name = "معالجة لاحقة")]
    PostProcessing = 9,
    [Display(Name = "مكتمل")]
    Completed = 10,
    [Display(Name = "مؤرشف")]
    Archived = 11,
    [Display(Name = "ملغى")]
    Cancelled = 12
}
