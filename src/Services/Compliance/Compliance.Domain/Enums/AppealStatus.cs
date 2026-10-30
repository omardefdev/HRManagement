using System.ComponentModel.DataAnnotations;

namespace Compliance.Domain.Enums;

public enum AppealStatus
{
    [Display(Name = "لا يوجد")]
    None,
    [Display(Name = "معلق")]
    Pending,
    [Display(Name = "مُبتَ فيه")]
    Decided,
}
