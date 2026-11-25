using System.ComponentModel.DataAnnotations;
using HRManagement.ServiceDefaults.Localization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Features.ManualInputs;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Web.ViewModels;

/// <summary>
/// PLAN-54 (§4): the forms of a manual pay input are allowlists: the employee, the type, the amount, the dates, the reason. Nothing
/// else can be posted: not the run an input applies to, not its approval state, not who entered or approved it. Amounts bind with the
/// invariant decimal binder.
/// </summary>
public sealed class ManualAllowanceCreateViewModel
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid? EmployeeId { get; set; }

    [Required(ErrorMessage = "نوع البدل مطلوب")]
    [Display(Name = "نوع البدل")]
    public Guid? AllowanceTypeId { get; set; }

    [Required(ErrorMessage = "المبلغ مطلوب")]
    [Display(Name = "المبلغ")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "تاريخ السريان مطلوب")]
    [Display(Name = "ساري من")]
    [DataType(DataType.Date)]
    public DateOnly? EffectiveFrom { get; set; }

    [Display(Name = "ساري إلى")]
    [DataType(DataType.Date)]
    public DateOnly? EffectiveTo { get; set; }

    [Required(ErrorMessage = "السبب مطلوب"), MaxLength(ManualPayInputRules.ReasonMaxLength)]
    [Display(Name = "السبب")]
    public string? Reason { get; set; }
}

public sealed class ManualDeductionCreateViewModel
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid? EmployeeId { get; set; }

    [Required(ErrorMessage = "نوع الخصم مطلوب"), MaxLength(100)]
    [Display(Name = "نوع الخصم")]
    public string? DeductionType { get; set; }

    [Required(ErrorMessage = "المبلغ مطلوب")]
    [Display(Name = "المبلغ")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal? Amount { get; set; }

    [Display(Name = "تاريخ الاستحقاق")]
    [DataType(DataType.Date)]
    public DateOnly? DueDate { get; set; }

    [Required(ErrorMessage = "السبب مطلوب"), MaxLength(ManualPayInputRules.ReasonMaxLength)]
    [Display(Name = "السبب")]
    public string? Reason { get; set; }
}

public sealed class ManualAdjustmentCreateViewModel
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid? EmployeeId { get; set; }

    [Required(ErrorMessage = "المبلغ مطلوب")]
    [Display(Name = "المبلغ (موجب أو سالب)")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "سبب التسوية مطلوب"), MaxLength(ManualPayInputRules.ReasonMaxLength)]
    [Display(Name = "سبب التسوية")]
    public string? AdjustmentReason { get; set; }

    /// <summary>
    /// PLAN-56 (G-PAY-03): the posted run this adjustment corrects, if any. Empty for an ordinary adjustment, which the next regular
    /// run takes; with a run chosen, only that run's correction takes it.
    /// </summary>
    [Display(Name = "تصحيح لتشغيل مرحّل (اختياري)")]
    public Guid? TargetSourceRunId { get; set; }
}

/// <summary>
/// The approval cell of a manual input's row: its state, who decided, and the approve and reject forms for a person who may decide
/// it (a holder of the approval policy who did not enter it).
/// </summary>
public sealed record ManualInputDecisionCell(
    string Controller,
    Guid InputId,
    ManualInputApprovalStatus Status,
    ManualInputOrigin Origin,
    Guid? EnteredBy,
    Guid? DecidedBy,
    string? DecisionNote,
    bool CanDecide)
{
    public static ManualInputDecisionCell For(string controller, IManualPayInput input, bool userMayApprove, Guid? userId) => new(
        controller,
        input.Id,
        input.ApprovalStatus,
        input.Origin,
        input.CreatedBy,
        input.ApprovedBy,
        input.DecisionNote,
        userMayApprove
        && input.Origin == ManualInputOrigin.Manual
        && input.ApprovalStatus == ManualInputApprovalStatus.PendingApproval
        && input.CreatedBy != userId);
}

/// <summary>What the three manual input controllers share: the caller, and the one mapping from a command's answer to a page.</summary>
public static class ManualInputPage
{
    public const string ErrorKey = "ManualInputError";
    public const string CanApproveKey = "ManualInputCanApprove";
    public const string UserIdKey = "ManualInputUserId";

    public static string Created(ManualInputResult result) =>
        result.Status == ManualInputApprovalStatus.PendingApproval
            ? "سُجّل المدخل وهو بانتظار اعتماد شخص ثانٍ؛ لا يدخل أي حساب قبل اعتماده."
            : "سُجّل المدخل.";
}
