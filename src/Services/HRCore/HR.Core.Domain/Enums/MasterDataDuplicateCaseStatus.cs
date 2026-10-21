using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

public enum MasterDataDuplicateCaseStatus
{
    [Display(Name = "مفتوح")]
    Open = 0,
    [Display(Name = "قيد المراجعة")]
    UnderReview = 1,
    [Display(Name = "مدمج")]
    Merged = 2,
    [Display(Name = "مرفوض")]
    Rejected = 3
}
