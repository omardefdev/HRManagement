using System.ComponentModel.DataAnnotations;

namespace Compliance.Domain.Enums;

public enum CaseStatus
{
    [Display(Name = "مفتوح")]
    Open,
    [Display(Name = "جاري")]
    Ongoing,
    [Display(Name = "مغلق")]
    Closed,
}