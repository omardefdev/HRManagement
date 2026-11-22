using HRManagement.ServiceDefaults;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.PayProfileChanges;

/// <summary>Why a salary change command was refused, and so its HTTP answer (the same for the form, the API and the PUT alias).</summary>
public enum PayProfileChangeFailure
{
    None = 0,

    /// <summary>A broken input rule (bounds, date, reason, legal reference, no active profile): 400.</summary>
    Invalid = 1,

    /// <summary>A state conflict (a second open request, a concurrent decision, a frozen month, a wrong state): 409.</summary>
    Conflict = 2,

    /// <summary>Deciding one's own request: 403.</summary>
    SelfDecision = 3,

    /// <summary>Cancelling somebody else's request without being an approver: 403.</summary>
    Forbidden = 4,

    /// <summary>No such request or employee in the caller's agency and branch scope: 404.</summary>
    NotFound = 5,
}

/// <summary>
/// The answer of every salary change command: the plan's <c>(Success, RequestId, State, Error)</c>, and the kind of refusal. A refusal
/// leaves the request as it was: <see cref="State"/> is then its unchanged state, and <see cref="Error"/> an Arabic message.
/// </summary>
public sealed record PayProfileChangeResult(
    bool Success,
    Guid? RequestId,
    PayProfileChangeState? State,
    string? Error,
    PayProfileChangeFailure Failure = PayProfileChangeFailure.None)
{
    public static PayProfileChangeResult Ok(Guid requestId, PayProfileChangeState state) => new(true, requestId, state, null);

    public static PayProfileChangeResult Fail(PayProfileChangeFailure failure, string error, Guid? requestId = null, PayProfileChangeState? state = null) =>
        new(false, requestId, state, error, failure);

    /// <summary>The HTTP status of a refusal.</summary>
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

/// <summary>The Arabic messages of the salary change transaction (rule 4: a refusal is said in Arabic and changes nothing).</summary>
public static class PayProfileChangeMessages
{
    public const string SelfDecision = "لا يجوز اعتماد الطلب من طالبه";
    public const string NotFound = "طلب تغيير الراتب غير موجود";
    public const string EmployeeNotFound = "الموظف غير موجود ضمن نطاقك";
    public const string NoActiveProfile = "لا يوجد ملف أجر فعّال للموظف";
    public const string ReasonRequired = "سبب التغيير إلزامي";
    public const string ReasonTooLong = "سبب التغيير أطول من 500 حرف";
    public const string LegalReferenceRequired = "المرجع القانوني إلزامي لطلب تغيير الراتب";
    public const string LegalReferenceTooLong = "المرجع القانوني أطول من 200 حرف";
    public const string SourceReferenceTooLong = "مرجع المصدر أطول من 100 حرف";
    public const string NothingProposed = "لم يُقترح تغيير: اذكر راتباً أساسياً أو درجة تختلف عن الحالية";
    public const string SalaryNotPositive = "الراتب الأساسي المقترح يجب أن يكون أكبر من صفر";
    public const string GradeNotFound = "الدرجة المقترحة غير موجودة";
    public const string MonthStartRequired = "تاريخ السريان يجب أن يكون أول يوم من شهر";
    public const string BeforeCurrentMonth = "تاريخ السريان لا يسبق الشهر الجاري";
    public const string BeforeCurrentVersion = "تاريخ السريان يسبق بداية ملف الأجر الحالي للموظف";
    public const string OpenRequestExists = "للموظف طلب تغيير راتب مفتوح؛ ألغِه قبل تقديم طلب جديد";
    public const string NotPending = "الطلب ليس بانتظار الاعتماد";
    public const string NotOpen = "الطلب ليس مفتوحاً؛ لا يُلغى إلا طلب بانتظار الاعتماد أو مجدول";
    public const string DecidedConcurrently = "تغيّر الطلب للتو من مستخدم آخر؛ أعد فتحه";
    public const string RejectNoteRequired = "ملاحظة الرفض إلزامية";
    public const string NoteTooLong = "الملاحظة أطول من 500 حرف";
    public const string CancelReasonRequired = "سبب الإلغاء إلزامي";
    public const string CancelNotAllowed = "لا يلغي الطلب إلا طالبه أو معتمد رواتب";
    public const string ManualSourceOnly = "مصدر الطلب يجب أن يكون يدوياً أو من الواجهة البرمجية";
    public const string AutomaticSourceOnly = "التغيير الآلي لا يكون مصدره يدوياً ولا من الواجهة البرمجية";
    public const string ApplyFailed = "تعذّر تطبيق التغيير لخطأ داخلي؛ سيُعاد في الدورة القادمة";

    // Numbers and the month are written with the invariant culture: the same text whatever the request's culture (an Arabic culture
    // would put its own separator and a direction mark inside "2026/12").
    public static string OutOfGradeBounds(string gradeCode, decimal min, decimal max) =>
        FormattableString.Invariant($"الراتب المقترح خارج حدود الدرجة {gradeCode}: الحد الأدنى {min:0.##} والحد الأعلى {max:0.##}");

    public static string FrozenMonth(DateOnly effectiveFrom) =>
        FormattableString.Invariant(
            $"لا يُطبَّق التغيير في شهر {effectiveFrom:yyyy/MM}: لهذا الشهر تشغيل رواتب مجمّد أو معتمد أو مرحّل أو قيد الحساب يشمل الموظف");
}

/// <summary>The caller's branch scope, as the handlers read it: a unit is visible to an unrestricted caller, or when it is in his scope.
/// A record with no unit is central: a restricted caller never sees it (PLAN-52, D-52-1).</summary>
public static class PayProfileChangeScope
{
    public static bool Sees(OrgAccessScope? scope, Guid? orgUnitId) =>
        scope is null || !scope.MustFilter
        || (!scope.IsDenied && orgUnitId is Guid unit && scope.AllowedOrgUnitIds.Contains(unit));
}
