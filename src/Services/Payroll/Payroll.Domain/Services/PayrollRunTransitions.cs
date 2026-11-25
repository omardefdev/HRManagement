using Payroll.Domain.Enums;

namespace Payroll.Domain.Services;

/// <summary>PLAN-56 (G-PAY-06): the acts that move a payroll run from one state to another. Each is one method of the run.</summary>
public enum PayrollRunAct
{
    /// <summary>A calculation starts, or starts again on a run whose results are not frozen.</summary>
    BeginCalculating,

    /// <summary>The calculator's own step: every partition is complete, the totals are being checked.</summary>
    BeginValidating,

    /// <summary>The calculator's own step: a partition or the validation failed; the run waits for a resume or a restart.</summary>
    MarkCalculationFailed,

    /// <summary>The lines exist and have a total; not yet validated.</summary>
    MarkCalculated,

    /// <summary>The validation passed: the total is frozen with the lines.</summary>
    MarkValidated,

    SubmitForReview,
    Approve,

    /// <summary>The reviewer sends the run back.</summary>
    Reject,

    Post,
    Reverse,

    /// <summary>The run is abandoned before its posting.</summary>
    Cancel,
}

/// <summary>
/// PLAN-56 (G-PAY-06): the legal transitions of a payroll run, in one table. No run moves between two states but through a method of
/// <see cref="Entities.PayrollRun"/>, and every such method asks <see cref="EnsureTransition"/> first:
/// <code>
/// Draft → Calculating → Calculated (validated) → UnderReview → Approved → Posted → Reversed
///                                                  UnderReview → (rejected)
/// any state before the posting → Cancelled
/// </code>
/// with the calculator's own steps (<c>Validating</c>, and a failure back to <c>Calculating</c>). <c>Posted</c> leaves only by a
/// reversal; <c>Reversed</c> and <c>Cancelled</c> are final.
/// </summary>
public static class PayrollRunTransitions
{
    /// <summary>One act: the states it may leave, the state it reaches, and what it answers from any other state.</summary>
    public sealed record Rule(PayrollRunAct Act, IReadOnlyList<PayrollRunStatus> From, PayrollRunStatus To, string Refusal);

    private static readonly PayrollRunStatus[] BeforeThePosting =
    [
        PayrollRunStatus.Draft,
        PayrollRunStatus.Calculating,
        PayrollRunStatus.Validating,
        PayrollRunStatus.Calculated,
        PayrollRunStatus.UnderReview,
        PayrollRunStatus.Approved,
    ];

    public static readonly IReadOnlyList<Rule> Rules =
    [
        new(PayrollRunAct.BeginCalculating,
            [PayrollRunStatus.Draft, PayrollRunStatus.Calculated, PayrollRunStatus.Calculating],
            PayrollRunStatus.Calculating,
            "لا يمكن حساب البنود إلا في حالة مسودة أو محسوب أو قيد الحساب."),
        new(PayrollRunAct.BeginValidating,
            [PayrollRunStatus.Calculating],
            PayrollRunStatus.Validating,
            "لا يبدأ التحقق من الإجماليات إلا بعد اكتمال الحساب."),
        new(PayrollRunAct.MarkCalculationFailed,
            [PayrollRunStatus.Calculating, PayrollRunStatus.Validating],
            PayrollRunStatus.Calculating,
            "لا يُسجَّل فشل الحساب إلا لتشغيل قيد الحساب أو قيد التحقق."),
        new(PayrollRunAct.MarkCalculated,
            [PayrollRunStatus.Draft, PayrollRunStatus.Calculated, PayrollRunStatus.Calculating, PayrollRunStatus.Validating],
            PayrollRunStatus.Calculated,
            "لا يمكن إنهاء الحساب في الحالة الحالية."),
        new(PayrollRunAct.MarkValidated,
            [PayrollRunStatus.Calculating, PayrollRunStatus.Calculated, PayrollRunStatus.Validating],
            PayrollRunStatus.Calculated,
            "لا يمكن التحقق من تشغيل إلا بعد حسابه وقبل إرساله للمراجعة؛ التحقق لا يعيد تشغيلاً أُرسل أو اعتُمد أو رُحّل أو عُكس أو أُلغي."),
        new(PayrollRunAct.SubmitForReview,
            [PayrollRunStatus.Calculated],
            PayrollRunStatus.UnderReview,
            "يجب إكمال الحساب والتحقق من الإجماليات قبل الإرسال للمراجعة."),
        new(PayrollRunAct.Approve,
            [PayrollRunStatus.UnderReview],
            PayrollRunStatus.Approved,
            "لا يمكن الاعتماد إلا للتشغيلات قيد المراجعة."),
        new(PayrollRunAct.Reject,
            [PayrollRunStatus.UnderReview],
            PayrollRunStatus.Calculated,
            "لا يمكن الرفض إلا للتشغيلات قيد المراجعة."),
        new(PayrollRunAct.Post,
            [PayrollRunStatus.Approved],
            PayrollRunStatus.Posted,
            "يجب اعتماد التشغيل قبل الترحيل."),
        new(PayrollRunAct.Reverse,
            [PayrollRunStatus.Posted],
            PayrollRunStatus.Reversed,
            "لا يمكن عكس إلا تشغيلاً مرحّلاً."),
        new(PayrollRunAct.Cancel,
            BeforeThePosting,
            PayrollRunStatus.Cancelled,
            "لا يمكن إلغاء تشغيل مرحّل أو معكوس أو ملغى."),
    ];

    private static readonly IReadOnlyDictionary<PayrollRunAct, Rule> ByAct = Rules.ToDictionary(r => r.Act);

    public static Rule For(PayrollRunAct act) => ByAct[act];

    public static bool CanApply(PayrollRunAct act, PayrollRunStatus from) => ByAct[act].From.Contains(from);

    /// <summary>Null when <paramref name="act"/> may leave <paramref name="from"/>; otherwise its refusal.</summary>
    public static string? Refusal(PayrollRunAct act, PayrollRunStatus from) => CanApply(act, from) ? null : ByAct[act].Refusal;

    /// <summary>The state <paramref name="act"/> reaches from <paramref name="from"/>, or an <see cref="InvalidOperationException"/> with its refusal.</summary>
    public static PayrollRunStatus EnsureTransition(PayrollRunAct act, PayrollRunStatus from)
    {
        var rule = ByAct[act];
        if (!rule.From.Contains(from))
            throw new InvalidOperationException(rule.Refusal);
        return rule.To;
    }
}
