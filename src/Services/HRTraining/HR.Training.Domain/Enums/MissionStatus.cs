using System.ComponentModel.DataAnnotations;

namespace HR.Training.Domain.Enums;

public enum MissionStatus
{
    [Display(Name = "مقترح")]
    Proposed,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "جاري")]
    Ongoing,
    [Display(Name = "مكتمل")]
    Completed,
}
