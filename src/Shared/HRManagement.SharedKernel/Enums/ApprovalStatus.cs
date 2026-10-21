using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Enums;

public enum ApprovalStatus
{
    [Display(Name = "معلق")]
    Pending,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "مرفوض")]
    Rejected,
    [Display(Name = "ملغى")]
    Cancelled,
    [Display(Name = "منفّذ")]
    Executed
}
