using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-56 (G-PAY-03): a cycle with positive and negative adjustments validates, and what is posted equals what was approved. The
/// posting checks and writes nothing: it refuses lines that changed since the validation, and it assigns no amount of any line.
/// </summary>
[Collection("IntegrationSerial")]
public class Pay056PostingTests
{
    private static string Amounts(IEnumerable<Payroll.Domain.Entities.SalaryLine> lines) =>
        string.Join(";", lines.OrderBy(l => l.EmployeeId).Select(l =>
            FormattableString.Invariant($"{l.EmployeeId:N}|{l.GrossPay}|{l.TotalDeductions}|{l.TotalAdjustments}|{l.NetPay}")));

    [Fact]
    public async Task A_cycle_with_a_positive_and_a_negative_adjustment_validates_and_what_is_posted_equals_what_was_approved()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var raised = await scope.EmployeeAsync();
        var lowered = await scope.EmployeeAsync();
        var untouched = await scope.EmployeeAsync();
        await scope.AdjustmentAsync(raised, 50_000m, "فرق نقل");
        await scope.AdjustmentAsync(lowered, -20_000m, "تسوية يدوية");

        // The calculation validates: before this plan any adjustment with an amount failed the whole cycle.
        var runId = await scope.CalculatedRunAsync();
        var validated = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Calculated, validated.Status);
        Assert.True(validated.ValidatedAtCalculate);
        Assert.True(validated.ResultsFrozen);

        var lines = await scope.LinesAsync(runId);
        var plain = lines.Single(l => l.EmployeeId == untouched);
        Assert.Equal(0m, plain.TotalAdjustments);
        Assert.Equal(50_000m, lines.Single(l => l.EmployeeId == raised).TotalAdjustments);
        Assert.Equal(-20_000m, lines.Single(l => l.EmployeeId == lowered).TotalAdjustments);
        // The same salary: an adjustment moves the net by its amount, and neither the gross nor the deductions (D-PAY-17).
        Assert.Equal(plain.NetPay + 50_000m, lines.Single(l => l.EmployeeId == raised).NetPay);
        Assert.Equal(plain.NetPay - 20_000m, lines.Single(l => l.EmployeeId == lowered).NetPay);
        Assert.All(lines, l => Assert.Equal(plain.GrossPay, l.GrossPay));
        Assert.All(lines, l => Assert.Equal(plain.TotalDeductions, l.TotalDeductions));
        Assert.All(lines, l => Assert.Equal(PayrollNet.Net(l), l.NetPay));
        Assert.Equal(lines.Sum(l => l.NetPay), validated.TotalNetPay);

        await scope.SubmitAsync(runId);
        await scope.ApproveAsync(runId);
        await scope.CloseAuditAsync(runId);
        var approved = await scope.RunAsync(runId);
        var amountsApproved = Amounts(lines);

        var (posted, error) = await scope.PostAsync(runId);
        Assert.True(posted, error);

        // What is posted is what was approved: the run's total, every line's figures, the batch and the event's amount.
        var after = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Posted, after.Status);
        Assert.Equal(approved.TotalNetPay, after.TotalNetPay);
        Assert.Equal(amountsApproved, Amounts(await scope.LinesAsync(runId)));
        var batch = await scope.Db.PaymentBatches.AsNoTracking().SingleAsync(b => b.PayrollRunId == runId);
        Assert.Equal(approved.TotalNetPay, batch.TotalAmount);
        Assert.Equal(PaymentBatchStatus.Pending, batch.Status);
    }

    [Fact]
    public async Task The_posting_writes_no_line()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        await scope.AdjustmentAsync(employee, 12_500m);
        var runId = await scope.ApprovedRunAsync();
        var before = await scope.Db.SalaryLines.AsNoTracking().SingleAsync(l => l.PayrollRunId == runId);

        var (posted, error) = await scope.PostAsync(runId);
        Assert.True(posted, error);

        var after = await scope.Db.SalaryLines.AsNoTracking().SingleAsync(l => l.PayrollRunId == runId);
        Assert.Equal(before.NetPay, after.NetPay);
        Assert.Equal(before.GrossPay, after.GrossPay);
        Assert.Equal(before.TotalDeductions, after.TotalDeductions);
        Assert.Equal(before.TotalAdjustments, after.TotalAdjustments);
        // Not even its bookkeeping: a line the posting saved would carry a later update stamp.
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }

    [Fact]
    public async Task The_posting_refuses_a_line_whose_net_changed_since_the_validation_and_changes_nothing()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        await scope.EmployeeAsync();
        await scope.AdjustmentAsync(employee, 50_000m);
        var runId = await scope.ApprovedRunAsync();

        // Somebody changes a frozen line around the application: the net an older posting would have written (no adjustment).
        await scope.Db.Database.ExecuteSqlRawAsync(
            """UPDATE "SalaryLines" SET "NetPay" = "NetPay" - 50000 WHERE "PayrollRunId" = {0} AND "EmployeeId" = {1}""", runId, employee);

        var (posted, error) = await scope.PostAsync(runId);
        Assert.False(posted);
        Assert.StartsWith("تغيّرت النتائج بعد التحقق", error);

        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Approved, run.Status);
        Assert.Empty(await scope.Db.PaymentBatches.AsNoTracking().Where(b => b.PayrollRunId == runId).ToListAsync());
    }

    [Fact]
    public async Task The_posting_refuses_lines_that_are_sound_but_no_longer_add_up_to_the_frozen_total()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.ApprovedRunAsync();

        // The frozen total is moved; every line is still sound in itself.
        await scope.Db.Database.ExecuteSqlRawAsync("""UPDATE "PayrollRuns" SET "TotalNetPay" = "TotalNetPay" + 1 WHERE "Id" = {0}""", runId);

        var (posted, error) = await scope.PostAsync(runId);
        Assert.False(posted);
        Assert.StartsWith("تغيّرت النتائج بعد التحقق", error);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
        Assert.Empty(await scope.Db.PaymentBatches.AsNoTracking().Where(b => b.PayrollRunId == runId).ToListAsync());
    }

    [Fact]
    public async Task The_posting_refuses_a_line_that_was_added_after_the_validation()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var runId = await scope.ApprovedRunAsync();

        // A line with no components, as the removed manual page made them.
        scope.Db.SalaryLines.Add(new Payroll.Domain.Entities.SalaryLine
        {
            PayrollRunId = runId,
            EmployeeId = Guid.NewGuid(),
            GrossPay = 1_000m,
            TotalDeductions = 100m,
            NetPay = 900m,
        });
        await scope.Db.SaveChangesAsync();

        var (posted, error) = await scope.PostAsync(runId);
        Assert.False(posted);
        Assert.StartsWith("تغيّرت النتائج بعد التحقق", error);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(runId)).Status);
    }

    [Fact]
    public async Task A_run_with_no_adjustment_posts_the_total_it_always_posted()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync(900_000m);
        await scope.EmployeeAsync(650_000m);
        var runId = await scope.ApprovedRunAsync();
        var lines = await scope.LinesAsync(runId);

        var (posted, error) = await scope.PostAsync(runId);
        Assert.True(posted, error);

        // The total every release before this plan posted: the sum over the lines of max(0, gross − deductions), two places.
        var before = lines.Sum(l => decimal.Round(Math.Max(0m, l.GrossPay + (-l.TotalDeductions)), 2, MidpointRounding.AwayFromZero));
        Assert.Equal(before, (await scope.RunAsync(runId)).TotalNetPay);
        Assert.All(lines, l => Assert.Equal(0m, l.TotalAdjustments));
    }
}
