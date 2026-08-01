using System.ComponentModel.DataAnnotations;

namespace HR.Core.Web.ViewModels;

public class ApprovalDelegationCreateViewModel
{
    [Required]
    [Display(Name = "المفوَّض إليه")]
    public Guid DelegateId { get; set; }

    [Display(Name = "نطاق نوع الكيان")]
    [StringLength(128)]
    public string? EntityTypeScope { get; set; }

    [Display(Name = "نطاق الدور")]
    [StringLength(100)]
    public string? RoleScope { get; set; }

    [Display(Name = "يبدأ من")]
    public DateTime ActiveFrom { get; set; }

    [Display(Name = "ينتهي في")]
    public DateTime ActiveUntil { get; set; }

    [Required]
    [StringLength(500)]
    [Display(Name = "السبب")]
    public string Reason { get; set; } = string.Empty;
}
