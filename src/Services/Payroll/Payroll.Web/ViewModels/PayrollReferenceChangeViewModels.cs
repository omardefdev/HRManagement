using System.ComponentModel.DataAnnotations;
using HRManagement.ServiceDefaults.Localization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Features.ReferenceChanges;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Web.ViewModels;

/// <summary>What every reference change request states: why, and on which legal basis (D-53-4).</summary>
public abstract class PayrollReferenceChangeFieldsViewModel
{
    [Required(ErrorMessage = "سبب التغيير إلزامي")]
    [MaxLength(PayProfileChangeRequest.ReasonMaxLength, ErrorMessage = "سبب التغيير أطول من 500 حرف")]
    [Display(Name = "سبب التغيير")]
    public string? Reason { get; set; }

    [Required(ErrorMessage = "المرجع القانوني إلزامي لتغيير السلّم أو الدرجة")]
    [MaxLength(PayProfileChangeRequest.LegalReferenceMaxLength, ErrorMessage = "المرجع القانوني أطول من 200 حرف")]
    [Display(Name = "المرجع القانوني")]
    public string? LegalReference { get; set; }
}

/// <summary>PLAN-53: the fields of a salary scale a request may propose. No entity is bound.</summary>
public sealed class SalaryScaleChangeViewModel : PayrollReferenceChangeFieldsViewModel
{
    /// <summary>The scale being changed (the route's id); empty for a new scale.</summary>
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "كود الجدول إلزامي"), MaxLength(50)]
    [Display(Name = "الكود")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "اسم الجدول إلزامي"), MaxLength(200)]
    [Display(Name = "الاسم بالعربية")]
    public string? NameAr { get; set; }

    [Required(ErrorMessage = "تاريخ بداية الجدول إلزامي")]
    [DataType(DataType.Date)]
    [Display(Name = "ساري من")]
    public DateOnly? EffectiveFrom { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "ساري إلى")]
    public DateOnly? EffectiveTo { get; set; }

    [Display(Name = "فعّال")]
    public bool IsActive { get; set; } = true;

    public SalaryScaleChange ToChange() => new(Code ?? string.Empty, NameAr ?? string.Empty, EffectiveFrom ?? default, EffectiveTo, IsActive);

    public static SalaryScaleChangeViewModel From(SalaryScale scale)
    {
        var change = SalaryScaleChange.From(scale);
        return new SalaryScaleChangeViewModel
        {
            Id = scale.Id,
            Code = change.Code,
            NameAr = change.NameAr,
            EffectiveFrom = change.EffectiveFrom,
            EffectiveTo = change.EffectiveTo,
            IsActive = change.IsActive,
        };
    }
}

/// <summary>PLAN-53: the fields of a grade a request may propose. No entity is bound.</summary>
public sealed class PayGradeChangeViewModel : PayrollReferenceChangeFieldsViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "اختر جدول الرواتب")]
    [Display(Name = "جدول الرواتب")]
    public Guid? SalaryScaleId { get; set; }

    [Required(ErrorMessage = "كود الدرجة إلزامي"), MaxLength(50)]
    [Display(Name = "كود الدرجة")]
    public string? GradeCode { get; set; }

    [Required(ErrorMessage = "اسم الدرجة إلزامي"), MaxLength(200)]
    [Display(Name = "اسم الدرجة")]
    public string? GradeNameAr { get; set; }

    [Display(Name = "الراتب الأساسي")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal BaseSalary { get; set; }

    [Display(Name = "الحد الأدنى")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal MinSalary { get; set; }

    [Display(Name = "الحد الأعلى")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal MaxSalary { get; set; }

    public PayGradeChange ToChange() =>
        new(SalaryScaleId ?? Guid.Empty, GradeCode ?? string.Empty, GradeNameAr ?? string.Empty, BaseSalary, MinSalary, MaxSalary);

    public static PayGradeChangeViewModel From(PayGrade grade) => new()
    {
        Id = grade.Id,
        SalaryScaleId = grade.SalaryScaleId,
        GradeCode = grade.GradeCode,
        GradeNameAr = grade.GradeNameAr,
        BaseSalary = grade.BaseSalary,
        MinSalary = grade.MinSalary,
        MaxSalary = grade.MaxSalary,
    };
}

/// <summary>PLAN-53: the request to delete a scale or a grade: the record is named by the route, and only the reason and the reference are posted.</summary>
public sealed class PayrollReferenceDeleteViewModel : PayrollReferenceChangeFieldsViewModel
{
    public Guid Id { get; set; }

    /// <summary>The record's code and name, for the page (never posted back as data).</summary>
    public string? Label { get; set; }
}

/// <summary>A scale or a grade with the request that waits on it, if any.</summary>
public sealed class PayGradeDetailsViewModel
{
    public required PayGrade Grade { get; init; }
    public PayrollReferenceChangeRequest? OpenRequest { get; init; }
    public int ProfilesNamingIt { get; init; }
    public int ProfilesOutsideBounds { get; init; }
    public bool CanChange { get; init; }
}

public sealed class SalaryScaleDetailsViewModel
{
    public required SalaryScale Scale { get; init; }
    public PayrollReferenceChangeRequest? OpenRequest { get; init; }
    public bool CanChange { get; init; }
}

/// <summary>One field of a reference change as the details page shows it: its Arabic name, the current value and the proposed one.</summary>
public sealed record PayrollReferenceChangeFieldRow(string Label, string Current, string Proposed)
{
    public bool Changed => !string.Equals(Current, Proposed, StringComparison.Ordinal);
}

public sealed class PayrollReferenceChangeListViewModel
{
    public IReadOnlyList<PayrollReferenceChangeRequest> Items { get; init; } = [];
    public PayrollReferenceChangeState? State { get; init; }
    public PayrollReferenceEntityType? EntityType { get; init; }
    public bool CanApprove { get; init; }
}

public sealed class PayrollReferenceChangeDetailsViewModel
{
    public required PayrollReferenceChangeRequest Request { get; init; }
    public IReadOnlyList<PayrollReferenceChangeFieldRow> Rows { get; init; } = [];
    public bool CanDecide { get; init; }
    public bool CanCancel { get; init; }
    public bool IsOwnRequest { get; init; }

    /// <summary>For a change of a grade's bounds: the active pay profiles that the proposed bounds would leave outside (none is changed).</summary>
    public int? ProfilesOutsideProposedBounds { get; init; }
}
