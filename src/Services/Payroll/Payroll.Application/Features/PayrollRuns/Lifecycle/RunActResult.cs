namespace Payroll.Application.Features.PayrollRuns.Lifecycle;

/// <summary>How an act on a payroll run ended. The web layer answers each with one HTTP status, everywhere (PLAN-56, P-4).</summary>
public enum RunActOutcome
{
    /// <summary>Done: 200.</summary>
    Done,

    /// <summary>A missing note or reason, or a state the act cannot leave: 400. Nothing changed.</summary>
    Invalid,

    /// <summary>The run, job or request does not exist for the caller's agency: 404.</summary>
    NotFound,

    /// <summary>The caller may not do this to a run in this state (D-PAY-18): 403. Nothing changed.</summary>
    Forbidden,

    /// <summary>Another request got there first (a second pending request, a second open correction): 409. Nothing changed.</summary>
    Conflict,
}

/// <summary>
/// PLAN-56: the answer of an act on a payroll run. It deconstructs like the older acts' <c>(Success, Error)</c> pair, and it also
/// says which kind of refusal it is, so that every controller answers the same refusal with the same status.
/// </summary>
public sealed record RunActResult(RunActOutcome Outcome, string? Error = null, Guid? Id = null)
{
    public bool Success => Outcome == RunActOutcome.Done;

    public static RunActResult Done(Guid? id = null) => new(RunActOutcome.Done, null, id);
    public static RunActResult Invalid(string error) => new(RunActOutcome.Invalid, error);
    public static RunActResult NotFound(string error) => new(RunActOutcome.NotFound, error);
    public static RunActResult Forbidden(string error) => new(RunActOutcome.Forbidden, error);
    public static RunActResult Conflict(string error) => new(RunActOutcome.Conflict, error);

    public void Deconstruct(out bool success, out string? error)
    {
        success = Success;
        error = Error;
    }
}

/// <summary>The Arabic messages of the run's life that more than one act uses.</summary>
public static class RunLifecycleMessages
{
    public const string RunNotFound = "التشغيل غير موجود.";
    public const string JobNotFound = "المهمة غير موجودة.";

    /// <summary>
    /// PLAN-56 (P-7): a run with a funding decision or a partial payment batch is neither cancelled nor reversed here. What its
    /// arrears rows become when the run goes is the disbursement path's (PLAN-58, G-PAY-08).
    /// </summary>
    public const string HasFundingOrPartialDisbursement =
        "لهذا التشغيل قرار تمويل أو دفعة صرف جزئية؛ لا يُلغى ولا يُعكس من هنا. عالجه من مسار الصرف (قرارات التمويل والمتأخرات) أولاً.";

    /// <summary>D-PAY-18: after the run was sent for review, cancelling it is the approver's decision.</summary>
    public const string CancelAfterReviewNeedsApprover =
        "إلغاء تشغيل أُرسل للمراجعة أو اعتُمد قرار المعتمد لا المعالج (يتطلب صلاحية اعتماد الرواتب).";

    /// <summary>D-PAY-18: before the run is sent for review, cancelling it is the processor's act.</summary>
    public const string CancelBeforeReviewNeedsProcessor =
        "إلغاء تشغيل لم يُرسل للمراجعة بعد يتطلب صلاحية معالجة الرواتب.";
}
