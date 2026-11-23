namespace Payroll.Application.Options;

/// <summary>PLAN-53 (D-53-1, D-53-2, D-PAY-6): the rules of a manual salary change request. Section <c>Payroll:SalaryChange</c>.</summary>
public sealed class PayProfileChangeOptions
{
    public const string SectionName = "Payroll:SalaryChange";

    /// <summary>D-53-1: the effective date of a manual or API request is the first day of a month.</summary>
    public bool RequireMonthStart { get; set; } = true;

    /// <summary>D-53-2: when false, a salary outside the grade's bounds is refused at the request, the approval and the activation.</summary>
    public bool AllowOutOfGradeBounds { get; set; }

    /// <summary>D-PAY-6: a manual or API request names its legal reference.</summary>
    public bool RequireLegalReference { get; set; } = true;

    public PayProfileChangeActivationOptions Activation { get; set; } = new();
}

/// <summary>PLAN-53 (P-7): the job that applies the scheduled requests that are due.</summary>
public sealed class PayProfileChangeActivationOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Minutes between two cycles (the plan's daily cron is an interval here: the repository has no cron library).</summary>
    public int IntervalMinutes { get; set; } = 60;
}

/// <summary>PLAN-53 (D-53-4): scales and grades change by a request another person approves. Section <c>Payroll:MasterData</c>.</summary>
public sealed class PayrollMasterDataOptions
{
    public const string SectionName = "Payroll:MasterData";

    /// <summary>True everywhere. False is honoured in Development only (elsewhere it is ignored, with a warning at start).</summary>
    public bool RequireApproval { get; set; } = true;
}
