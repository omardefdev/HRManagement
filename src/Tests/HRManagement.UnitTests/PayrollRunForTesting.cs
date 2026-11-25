using System.Reflection;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.TestSupport.PayrollRuns;

/// <summary>
/// PLAN-56 (G-PAY-06): a payroll run's state is given at construction and moved only by the run's own methods. A test that needs a
/// run in a state it did not reach by those methods (a planted job on a run «قيد الحساب», a reversed run for a refusal) sets the state
/// here. This file exists in the test projects only: product code has no way to assign a run's state, and an architecture test
/// (<c>Pay056TransitionTests</c>) keeps it so.
/// </summary>
internal static class ForTesting
{
    private static readonly FieldInfo StatusField =
        typeof(PayrollRun).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PayrollRun no longer keeps its state in the field _status; update ForTesting.SetStatus.");

    private static FieldInfo Field(string name) =>
        typeof(PayrollRun).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"PayrollRun no longer has the field {name}; update ForTesting.");

    /// <summary>
    /// Makes <paramref name="run"/> a run whose results are not frozen and not validated, around the run's methods (a planted
    /// calculation in progress on a run that was calculated before). Tests only.
    /// </summary>
    public static PayrollRun Unfreeze(PayrollRun run)
    {
        Field("_resultsFrozen").SetValue(run, false);
        Field("_validatedAtCalculate").SetValue(run, false);
        return run;
    }

    /// <summary>Puts <paramref name="run"/> in <paramref name="status"/> around its transitions. Tests only.</summary>
    public static PayrollRun SetStatus(PayrollRun run, PayrollRunStatus status)
    {
        StatusField.SetValue(run, status);
        return run;
    }
}
