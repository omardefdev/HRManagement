using System.ComponentModel.DataAnnotations;

namespace Compliance.Domain.Enums;

public enum DsarRequestType
{
    [Display(Name = "وصول")]
    Access = 0,
    [Display(Name = "تصحيح")]
    Rectify = 1,
    [Display(Name = "محو")]
    Deletion = 2
}

public enum DsarRequestStatus
{
    [Display(Name = "مفتوح")]
    Open = 0,
    [Display(Name = "قيد المعالجة")]
    InProgress = 1,
    [Display(Name = "مُنفَّذ")]
    Fulfilled = 2,
    [Display(Name = "مرفوض")]
    Rejected = 3
}
