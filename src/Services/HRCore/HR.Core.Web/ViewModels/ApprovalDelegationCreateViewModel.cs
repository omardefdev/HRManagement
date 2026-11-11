using System.ComponentModel.DataAnnotations;

namespace HR.Core.Web.ViewModels;

/// <summary>
/// PLAN-50 (D-50-2, D-WF-5): the delegate is an employee chosen from the directory; the role is one of the user's own delegable roles;
/// the entity type, when given, one of the closed list.
/// </summary>
public class ApprovalDelegationCreateViewModel
{
    [Required(ErrorMessage = "يجب اختيار المفوَّض من دليل الموظفين.")]
    [Display(Name = "المفوَّض إليه")]
    public Guid? DelegateEmployeeId { get; set; }

    [Required(ErrorMessage = "يجب تحديد الدور المفوَّض.")]
    [Display(Name = "الدور المفوَّض")]
    [StringLength(100)]
    public string? RoleScope { get; set; }

    [Display(Name = "نوع الكيان")]
    [StringLength(128)]
    public string? EntityTypeScope { get; set; }

    [Display(Name = "يبدأ من")]
    public DateTime ActiveFrom { get; set; }

    [Display(Name = "ينتهي في")]
    public DateTime ActiveUntil { get; set; }

    [Required(ErrorMessage = "سبب التفويض مطلوب.")]
    [StringLength(500)]
    [Display(Name = "السبب")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>The roles of the user's own token that may be delegated (D-50-1).</summary>
    public IReadOnlyList<string> DelegableRoles { get; set; } = [];

    /// <summary><c>Delegation:EntityTypes</c>.</summary>
    public IReadOnlyList<string> EntityTypes { get; set; } = [];
}
