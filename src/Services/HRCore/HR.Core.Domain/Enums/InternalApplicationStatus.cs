using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum InternalApplicationStatus
{
    [Display(Name = "مُقدَّم")]
    Submitted = 0,

    [Display(Name = "قيد المراجعة")]
    UnderReview = 1,

    [Display(Name = "قائمة مختصرة")]
    Shortlisted = 2,

    [Display(Name = "مقبول")]
    Accepted = 3,

    [Display(Name = "مرفوض")]
    Rejected = 4
}
