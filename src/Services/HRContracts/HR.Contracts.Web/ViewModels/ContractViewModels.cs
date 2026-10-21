using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Contracts.Web.ViewModels;

public class ContractCreateViewModel
{
    [Display(Name = "الموظف")]
    [Required(ErrorMessage = "اختيار الموظف مطلوب")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع العقد")]
    [Required(ErrorMessage = "نوع العقد مطلوب")]
    public Guid ContractTypeId { get; set; }

    [Display(Name = "تاريخ البداية")]
    [Required]
    [DataType(DataType.Date)]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [Display(Name = "تاريخ النهاية")]
    [DataType(DataType.Date)]
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Server-assigned. A new contract always starts <see cref="ContractStatus.Pending"/> and only
    /// reaches <see cref="ContractStatus.Active"/> through <c>ActivateContractCommand</c>, which
    /// runs the activation workflow and publishes the downstream events. Binding this from the
    /// form let anyone create an already-active contract and skip approval entirely.
    /// </summary>
    [BindNever]
    [ValidateNever]
    public ContractStatus Status { get; init; } = ContractStatus.Pending;

    [Display(Name = "البنود")]
    public string? Terms { get; set; }

    [Display(Name = "الموقّع")]
    [BindNever]
    [ValidateNever]
    public Guid? SignedBy { get; set; }

    [Display(Name = "تاريخ التوقيع")]
    [DataType(DataType.Date)]
    [BindNever]
    [ValidateNever]
    public DateOnly? SignedDate { get; set; }

    [Display(Name = "فترة الاختبار (أشهر)")]
    public int? ProbationMonths { get; set; }

    public SelectList? ContractTypes { get; set; }
}

public class ContractEditViewModel : ContractCreateViewModel
{
    public Guid Id { get; set; }
}
