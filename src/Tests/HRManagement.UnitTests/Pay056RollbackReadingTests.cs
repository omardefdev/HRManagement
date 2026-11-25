using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-56 (P-16, the rollback note): the batch's, the order's and the decision's states are stored as their names, and a release
/// that does not know a name cannot read the row: EF's converter throws. So after a rollback to PLAN-55's tag, every query of that
/// tag that reads a batch <c>Voided</c>, an order <c>Cancelled</c> or a decision <c>Cancelled</c> fails (the payment batches' list of
/// an agency with a voided batch, the page of a run with a cancellation among its decisions). The note in
/// <c>docs/OPS_VPS_SINGLE_HOST.md</c> says so: once this release cancelled or reversed a run, the way back is a fix forward, and a
/// rollback is a degraded state that lasts until the newer release runs again. This test keeps the two facts the note rests on.
/// </summary>
public class Pay056RollbackReadingTests
{
    private static object? Read<TEnum>(string stored) where TEnum : struct, Enum =>
        new EnumToStringConverter<TEnum>().ConvertFromProvider(stored);

    [Fact]
    public void A_state_name_a_release_does_not_know_cannot_be_read_by_it()
    {
        Assert.Throws<InvalidOperationException>(() => Read<PaymentBatchStatus>("AStateOfALaterRelease"));
        Assert.Throws<InvalidOperationException>(() => Read<PaymentOrderStatus>("AStateOfALaterRelease"));
        Assert.Throws<InvalidOperationException>(() => Read<ApprovalDecision>("AStateOfALaterRelease"));
        // A name it knows reads as itself.
        Assert.Equal(PaymentBatchStatus.Voided, Read<PaymentBatchStatus>(nameof(PaymentBatchStatus.Voided)));
    }

    [Fact]
    public void The_three_states_this_plan_adds_are_stored_as_names_and_the_runs_own_state_as_a_number_the_older_release_knows()
    {
        using var db = new PayrollDbContext(
            new DbContextOptionsBuilder<PayrollDbContext>().UseSqlite("Data Source=:memory:").Options);

        Assert.Equal(typeof(string), db.Model.FindEntityType(typeof(PaymentBatch))!.FindProperty(nameof(PaymentBatch.Status))!.GetProviderClrType());
        Assert.Equal(typeof(string), db.Model.FindEntityType(typeof(PaymentOrder))!.FindProperty(nameof(PaymentOrder.Status))!.GetProviderClrType());
        Assert.Equal(typeof(string), db.Model.FindEntityType(typeof(PayrollRunApproval))!.FindProperty(nameof(PayrollRunApproval.Decision))!.GetProviderClrType());
        // The run's own state is a number, and «cancelled» (8) is one PLAN-55's tag has: a cancelled run itself reads there.
        Assert.NotEqual(typeof(string), db.Model.FindEntityType(typeof(PayrollRun))!.FindProperty(nameof(PayrollRun.Status))!.GetProviderClrType());
        Assert.Equal(8, (int)PayrollRunStatus.Cancelled);
    }
}
