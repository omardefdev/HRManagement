using System.ComponentModel.DataAnnotations;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Web.ViewModels;

/// <summary>
/// PLAN-53: the salary change request form. An allowlist of what a processor may propose: no entity is bound, so the unit, the regime,
/// the bank account, the state and the requester never come from the browser.
/// </summary>
public sealed class PayProfileChangeRequestViewModel
{
    [Required(ErrorMessage = "اختر الموظف")]
    [Display(Name = "الموظف")]
    public Guid? EmployeeId { get; set; }

    // The limits are written with a dot: read them with the invariant culture, or an Arabic request culture fails to parse "0.01".
    [Range(typeof(decimal), "0.01", "999999999999", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "الراتب الأساسي المقترح يجب أن يكون أكبر من صفر")]
    [Display(Name = "الراتب الأساسي المقترح")]
    public decimal? ProposedBaseSalary { get; set; }

    [Display(Name = "الدرجة المقترحة")]
    public Guid? ProposedPayGradeId { get; set; }

    [Required(ErrorMessage = "تاريخ السريان إلزامي")]
    [DataType(DataType.Date)]
    [Display(Name = "يسري من (أول يوم من شهر)")]
    public DateOnly? EffectiveFrom { get; set; }

    [Required(ErrorMessage = "سبب التغيير إلزامي")]
    [MaxLength(PayProfileChangeRequest.ReasonMaxLength, ErrorMessage = "سبب التغيير أطول من 500 حرف")]
    [Display(Name = "سبب التغيير")]
    public string? Reason { get; set; }

    [MaxLength(PayProfileChangeRequest.LegalReferenceMaxLength, ErrorMessage = "المرجع القانوني أطول من 200 حرف")]
    [Display(Name = "المرجع القانوني")]
    public string? LegalReference { get; set; }
}

/// <summary>One request as the list and the details show it: the request, and the names of the grades it mentions.</summary>
public sealed record PayProfileChangeRow(PayProfileChangeRequest Request, string? CurrentGradeName, string? ProposedGradeName);

public sealed class PayProfileChangeListViewModel
{
    public IReadOnlyList<PayProfileChangeRow> Items { get; init; } = [];
    public PayProfileChangeState? State { get; init; }
    public Guid? EmployeeId { get; init; }
    public bool BlockedOnly { get; init; }
    public bool CanRequest { get; init; }
    public bool CanApprove { get; init; }
}

public sealed class PayProfileChangeDetailsViewModel
{
    public required PayProfileChangeRow Row { get; init; }

    /// <summary>The caller may approve or reject: he holds Payroll.Approve, the request is pending, and he is not its requester.</summary>
    public bool CanDecide { get; init; }

    /// <summary>The caller may cancel: the request is open, and he is its requester or an approver.</summary>
    public bool CanCancel { get; init; }

    /// <summary>The caller holds Payroll.Approve but is the requester: the page says why the buttons are absent.</summary>
    public bool IsOwnRequest { get; init; }
}
