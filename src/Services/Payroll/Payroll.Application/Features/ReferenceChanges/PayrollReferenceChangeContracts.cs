using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Options;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.ReferenceChanges;

/// <summary>The fields of a grade a request may propose: an allowlist, never the entity.</summary>
public sealed record PayGradeChange(
    Guid SalaryScaleId, string GradeCode, string GradeNameAr, decimal BaseSalary, decimal MinSalary, decimal MaxSalary)
{
    public static PayGradeChange From(PayGrade grade) =>
        new(grade.SalaryScaleId, grade.GradeCode, grade.GradeNameAr, grade.BaseSalary, grade.MinSalary, grade.MaxSalary);

    public void ApplyTo(PayGrade grade)
    {
        grade.SalaryScaleId = SalaryScaleId;
        grade.GradeCode = GradeCode;
        grade.GradeNameAr = GradeNameAr;
        grade.BaseSalary = BaseSalary;
        grade.MinSalary = MinSalary;
        grade.MaxSalary = MaxSalary;
    }
}

/// <summary>The fields of a salary scale a request may propose. The dates are calendar dates.</summary>
public sealed record SalaryScaleChange(string Code, string NameAr, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive)
{
    public static SalaryScaleChange From(SalaryScale scale) => new(
        scale.Code, scale.NameAr, DateOnly.FromDateTime(scale.EffectiveFrom),
        scale.EffectiveTo is DateTime to ? DateOnly.FromDateTime(to) : null, scale.IsActive);

    public void ApplyTo(SalaryScale scale)
    {
        scale.Code = Code;
        scale.NameAr = NameAr;
        scale.EffectiveFrom = Stored(EffectiveFrom);
        scale.EffectiveTo = EffectiveTo is DateOnly to ? Stored(to) : null;
        scale.IsActive = IsActive;
    }

    private static DateTime Stored(DateOnly date) => new(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
}

/// <summary>How the proposed and the current values are written in the request, and read back at the approval and on the screen.</summary>
public static class PayrollReferenceChangeJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Read<T>(string? json) where T : class =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>The answer of a reference change command, with the same refusal kinds and HTTP statuses as a salary change.</summary>
public sealed record PayrollReferenceChangeResult(
    bool Success,
    Guid? RequestId,
    PayrollReferenceChangeState? State,
    string? Error,
    PayProfileChangeFailure Failure = PayProfileChangeFailure.None)
{
    public static PayrollReferenceChangeResult Ok(Guid requestId, PayrollReferenceChangeState state) => new(true, requestId, state, null);

    public static PayrollReferenceChangeResult Fail(
        PayProfileChangeFailure failure, string error, Guid? requestId = null, PayrollReferenceChangeState? state = null) =>
        new(false, requestId, state, error, failure);

    public int StatusCode => Failure switch
    {
        PayProfileChangeFailure.None => 200,
        PayProfileChangeFailure.Invalid => 400,
        PayProfileChangeFailure.Conflict => 409,
        PayProfileChangeFailure.SelfDecision or PayProfileChangeFailure.Forbidden => 403,
        PayProfileChangeFailure.NotFound => 404,
        _ => 400,
    };
}

public static class PayrollReferenceChangeMessages
{
    public const string NotFound = "طلب تغيير البيانات المرجعية غير موجود";
    public const string RecordNotFound = "السجل المطلوب تغييره غير موجود";
    public const string ProposalRequired = "القيم المقترحة مطلوبة";
    public const string ReasonRequired = "سبب التغيير إلزامي";
    public const string ReasonTooLong = "سبب التغيير أطول من 500 حرف";
    public const string LegalReferenceRequired = "المرجع القانوني إلزامي لتغيير السلّم أو الدرجة";
    public const string LegalReferenceTooLong = "المرجع القانوني أطول من 200 حرف";
    public const string ScaleNotFound = "جدول الرواتب المحدد غير موجود";
    public const string GradeCodeRequired = "كود الدرجة واسمها إلزاميان";
    public const string GradeTextTooLong = "كود الدرجة أطول من 50 حرفاً أو اسمها أطول من 200";
    public const string GradeAmountsNegative = "مبالغ الدرجة لا تكون سالبة";
    public const string GradeBoundsOrder = "يجب أن يكون الحد الأدنى ≤ الراتب الأساسي ≤ الحد الأعلى";
    public const string GradeCodeTaken = "في هذا الجدول درجة بالكود نفسه";
    public const string ScaleCodeRequired = "كود الجدول واسمه إلزاميان";
    public const string ScaleTextTooLong = "كود الجدول أطول من 50 حرفاً أو اسمه أطول من 200";
    public const string ScaleDatesOrder = "تاريخ نهاية الجدول لا يسبق تاريخ بدايته";
    public const string ScaleCodeTaken = "يوجد جدول رواتب بالكود نفسه";
    public const string NothingChanges = "القيم المقترحة تطابق الحالية";
    public const string GradeInUse = "لا تُحذف الدرجة: تشير إليها ملفات أجر أو طلب تغيير راتب مفتوح";
    public const string ScaleHasGrades = "لا يُحذف جدول الرواتب: فيه درجات";
    public const string OpenRequestExists = "للسجل طلب تغيير مفتوح؛ ألغِه أو انتظر القرار فيه";
    public const string NotPending = "الطلب ليس بانتظار الاعتماد";
    public const string RejectNoteRequired = "ملاحظة الرفض إلزامية";
    public const string NoteTooLong = "الملاحظة أطول من 500 حرف";
    public const string CancelReasonRequired = "سبب الإلغاء إلزامي";
    public const string CancelNotAllowed = "لا يلغي الطلب إلا طالبه أو معتمد رواتب";
    public const string DecidedConcurrently = "تغيّر الطلب أو السجل للتو من مستخدم آخر؛ أعد فتحه";
}

/// <summary>
/// PLAN-53 (D-53-4): <c>Payroll:MasterData:RequireApproval</c> is true everywhere. False is honoured in Development only: there a
/// request is applied when it is submitted, by the same code. Anywhere else a false value is ignored, and the service says so at start.
/// </summary>
public static class PayrollMasterDataApproval
{
    public static bool IsRequired(PayrollMasterDataOptions options, IHostEnvironment? environment) =>
        options.RequireApproval || environment?.IsDevelopment() != true;

    /// <summary>The setting asks to skip the approval outside Development: it is not honoured.</summary>
    public static bool IsIgnored(PayrollMasterDataOptions options, IHostEnvironment? environment) =>
        !options.RequireApproval && environment?.IsDevelopment() != true;

    public const string IgnoredWarning =
        "Payroll:MasterData:RequireApproval=false is honoured in Development only and is ignored in {Environment}: salary scales and grades change by a request another person approves.";
}
