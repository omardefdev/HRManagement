using System.ComponentModel.DataAnnotations;

namespace IT.Infrastructure.Domain.Enums;

public enum BackupStatus
{
    [Display(Name = "ناجح")]
    Ok,
    [Display(Name = "فشل")]
    Failed,
}