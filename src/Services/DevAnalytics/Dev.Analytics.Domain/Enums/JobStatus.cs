using System.ComponentModel.DataAnnotations;

namespace Dev.Analytics.Domain.Enums;

public enum JobStatus
{
    [Display(Name = "ناجح")]
    Ok,
    [Display(Name = "فشل")]
    Failed,
}