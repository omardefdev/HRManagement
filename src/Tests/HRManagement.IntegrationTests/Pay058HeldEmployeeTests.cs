using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;
using HRManagement.TestSupport.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.BankAccounts;
using Payroll.Application.Features.Fraud;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-58 (§6, D-PAY-4, P-10): one held employee no longer holds back the agency. A hold of the run or with no run holds that
/// employee's order only, in every batch, and the others are sent; a service-regime blocker holds the blocked employee by name after a
/// full calculation (HoldEmployee), or refuses the calculation before the run moves (BlockRun); an employee with no pinned account is
/// held NO-ACCOUNT and, once two people verify his account, is pinned to it and sent alone by a supplementary file.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Pay058HeldEmployeeTests
{
    private static readonly Guid Sender = Pay056RunScope.Poster;

    private static async Task<Guid> WithAccountAsync(Pay056RunScope scope)
    {
        var employee = await scope.EmployeeAsync();
        scope.Db.Add(Pay055TestAccounts.Active(employee));
        await scope.Db.SaveChangesAsync();
        return employee;
    }

    private static async Task<Guid> BatchOfAsync(Pay056RunScope scope, Guid runId)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.PaymentBatches.AsNoTracking().Where(b => b.PayrollRunId == runId).Select(b => b.Id).SingleAsync();
    }

    private static async Task<Dictionary<Guid, (PaymentOrderStatus Status, string? Reason)>> OrdersAsync(Pay056RunScope scope, Guid batchId)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.PaymentOrders.AsNoTracking().Where(o => o.PaymentBatchId == batchId)
            .ToDictionaryAsync(o => o.EmployeeId, o => (o.Status, o.HoldReason));
    }

    [Fact]
    public async Task A_hold_with_no_run_holds_that_employees_order_in_every_batch_and_never_the_others()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var held = await WithAccountAsync(scope);
        var paid = await WithAccountAsync(scope);
        scope.Db.PaymentHolds.Add(new PaymentHold { EmployeeId = held, HoldType = PaymentHoldType.Manual, Reason = "PAY029-RANGE: مراجعة الراتب", Status = PaymentHoldStatus.Active });
        await scope.Db.SaveChangesAsync();

        foreach (var month in new[] { 1, 2 })
        {
            var batchId = await BatchOfAsync(scope, await scope.PostedRunAsync(2028, month));
            var sent = await scope.Mediator.Send(new ProcessPaymentBatchCommand(batchId, Sender));
            Assert.True(sent.Success, sent.Error);
            var orders = await OrdersAsync(scope, batchId);
            Assert.Equal((PaymentOrderStatus.Held, PaymentOrderHoldReasons.PaymentHold), orders[held]);
            Assert.Equal(PaymentOrderStatus.Submitted, orders[paid].Status);
        }
    }

    [Fact]
    public async Task A_regime_blocker_holds_the_blocked_employee_by_name_after_a_full_calculation_and_the_others_are_paid()
    {
        var hrCore = new FakeHrCoreServiceRegimeClient { ReasonCode = "REGIME_UNCLASSIFIED" };
        await using var scope = await Pay056RunScope.CreateAsync(services => services.AddSingleton<IHrCoreServiceRegimeClient>(hrCore));
        var blocked = await WithAccountAsync(scope);
        var paid = await WithAccountAsync(scope);
        hrCore.BlockedEmployeeIds.Add(blocked);

        var runId = await scope.PostedRunAsync();

        // Everyone was calculated; the blocked employee has a REGIME-BLOCKER hold of the run, with HR.Core's reason.
        Assert.Equal(new[] { blocked, paid }.Order(), (await scope.LinesAsync(runId)).Select(l => l.EmployeeId).Order());
        var hold = await scope.Db.PaymentHolds.AsNoTracking().SingleAsync(h => h.EmployeeId == blocked);
        Assert.Equal((PaymentHoldType.RegimeBlocker, (Guid?)runId, PaymentHoldStatus.Active), (hold.HoldType, hold.PayrollRunId, hold.Status));
        Assert.Contains("REGIME_UNCLASSIFIED", hold.Reason, StringComparison.Ordinal);

        var batchId = await BatchOfAsync(scope, runId);
        Assert.True((await scope.Mediator.Send(new ProcessPaymentBatchCommand(batchId, Sender))).Success);
        var orders = await OrdersAsync(scope, batchId);
        Assert.Equal((PaymentOrderStatus.Held, PaymentOrderHoldReasons.RegimeBlocker), orders[blocked]);
        Assert.Equal(PaymentOrderStatus.Submitted, orders[paid].Status);
    }

    [Fact]
    public async Task In_BlockRun_the_calculation_is_refused_before_the_run_moves_and_the_run_is_left_as_it_was()
    {
        var hrCore = new FakeHrCoreServiceRegimeClient();
        await using var scope = await Pay056RunScope.CreateAsync(services =>
        {
            services.AddSingleton<IHrCoreServiceRegimeClient>(hrCore);
            services.Configure<PayrollBlockerOptions>(o => o.Mode = PayrollBlockerMode.BlockRun);
        });
        var blocked = await WithAccountAsync(scope);
        await WithAccountAsync(scope);
        hrCore.BlockedEmployeeIds.Add(blocked);
        var runId = await scope.CreateRunAsync();

        var (ok, lines, error) = await scope.CalculateAsync(runId);

        Assert.False(ok);
        Assert.Equal(0, lines);
        Assert.Equal(PayrollRegimeGovernance.BlockRunRefusal(1), error);
        var run = await scope.RunAsync(runId);
        Assert.Equal((PayrollRunStatus.Draft, (Guid?)null), (run.Status, run.PayrollConfigurationId));
        Assert.Empty(await scope.Db.PayrollRunJobs.AsNoTracking().Where(j => j.PayrollRunId == runId).ToListAsync());
        Assert.Empty(await scope.Db.PaymentHolds.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task An_employee_with_no_account_is_held_and_his_verified_account_is_pinned_and_sent_alone_by_a_supplementary_file()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var paid = await WithAccountAsync(scope);
        var accountless = await scope.EmployeeAsync();
        var runId = await scope.PostedRunAsync();
        var batchId = await BatchOfAsync(scope, runId);
        Assert.True((await scope.Mediator.Send(new ProcessPaymentBatchCommand(batchId, Sender))).Success);
        Assert.Equal((PaymentOrderStatus.Held, PaymentOrderHoldReasons.NoAccount), (await OrdersAsync(scope, batchId))[accountless]);
        Assert.Equal(PaymentOrderStatus.Submitted, (await OrdersAsync(scope, batchId))[paid].Status);

        // Two people register and verify his account: no release request; the account is pinned on the line and on the unsent order.
        var registered = await scope.Mediator.Send(new RequestBankAccountCommand(
            accountless, BankAccountKind.Iban, Pay055TestAccounts.Iban(accountless, 3), null, "مصرف الاختبار", null, Pay056RunScope.Processor, null));
        Assert.True(registered.Success, registered.Error);
        var verified = await scope.Mediator.Send(new VerifyBankAccountCommand(
            accountless, registered.AccountId!.Value, "كتاب المصرف", Pay056RunScope.Approver, null));
        Assert.True(verified.Success, verified.Error);

        scope.Db.ChangeTracker.Clear();
        var order = await scope.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.PaymentBatchId == batchId && o.EmployeeId == accountless);
        Assert.Equal((PaymentOrderStatus.Held, (string?)null, Pay055TestAccounts.Iban(accountless, 3)), (order.Status, order.HoldReason, order.BeneficiaryIban));
        Assert.Equal(registered.AccountId, (await scope.Db.SalaryLines.AsNoTracking().SingleAsync(l => l.Id == order.SalaryLineId)).BankAccountId);
        var audit = await scope.Db.Set<EntityAuditLog>().AsNoTracking().SingleAsync(a => a.EntityId == order.Id && a.Action == HeldOrderRepinning.AuditAction);
        Assert.DoesNotContain(Pay055TestAccounts.Iban(accountless, 3), audit.ChangedFields ?? string.Empty, StringComparison.Ordinal);

        var supplementary = await scope.Mediator.Send(new SubmitHeldPaymentOrdersCommand(batchId, Sender));
        Assert.True(supplementary.Success, supplementary.Error);
        var file = await scope.Db.PaymentBatchFiles.AsNoTracking().SingleAsync(f => f.Id == supplementary.Id);
        Assert.Equal((PaymentBatchFileKind.HeldRelease, 1), (file.Kind, file.ControlCount));
        Assert.Equal(PaymentOrderStatus.Submitted, (await OrdersAsync(scope, batchId))[accountless].Status);
    }

    [Fact]
    public async Task Two_employees_paid_into_one_pinned_account_are_both_held_by_FR_04_and_the_others_are_sent()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var first = await WithAccountAsync(scope);
        var second = await scope.EmployeeAsync();
        var paid = await WithAccountAsync(scope);
        var shared = Pay055TestAccounts.Active(second);
        shared.AccountNumber = Pay055TestAccounts.Iban(first);           // the second employee's account carries the first one's number
        scope.Db.Add(shared);
        await scope.Db.SaveChangesAsync();
        var runId = await scope.PostedRunAsync();
        var batchId = await BatchOfAsync(scope, runId);

        Assert.True((await scope.Mediator.Send(new ProcessPaymentBatchCommand(batchId, Sender))).Success);

        var alerts = await scope.Db.FraudAlerts.AsNoTracking().Where(a => a.RuleId == FraudRuleIds.SharedIban).ToListAsync();
        Assert.Equal(new[] { first, second }.Order(), alerts.Select(a => a.EmployeeId).Order());
        var orders = await OrdersAsync(scope, batchId);
        Assert.Equal(PaymentOrderStatus.Held, orders[first].Status);
        Assert.Equal(PaymentOrderStatus.Held, orders[second].Status);
        Assert.Equal(PaymentOrderStatus.Submitted, orders[paid].Status);
    }

    [Fact]
    public async Task A_correction_line_raises_neither_FR_01_nor_FR_08_and_a_receipt_with_no_working_day_raises_FR_01()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await WithAccountAsync(scope);
        var ghost = await WithAccountAsync(scope);
        var sourceId = await scope.PostedRunAsync();
        var source = await scope.RunAsync(sourceId);
        // The period's receipts: the employee worked; the «ghost» was absent every working day of it.
        var start = DateOnly.FromDateTime(source.PeriodStart);
        var end = DateOnly.FromDateTime(source.PeriodEnd);
        scope.Db.AttendanceSummaryReceipts.Add(new AttendanceSummaryReceipt { SummaryId = Guid.NewGuid(), EmployeeId = employee, PeriodStart = start, PeriodEnd = end, LatestAbsentDays = 2 });
        scope.Db.AttendanceSummaryReceipts.Add(new AttendanceSummaryReceipt { SummaryId = Guid.NewGuid(), EmployeeId = ghost, PeriodStart = start, PeriodEnd = end, LatestAbsentDays = 31 });
        await scope.Db.SaveChangesAsync();

        Assert.True((await scope.Mediator.Send(new RunPayrollFraudScanCommand(sourceId))).Success);
        var fr01 = await scope.Db.FraudAlerts.AsNoTracking().Where(a => a.RuleId == FraudRuleIds.GhostEmployee).Select(a => a.EmployeeId).ToListAsync();
        Assert.Equal([ghost], fr01);

        // A correction of the run: its lines carry their source (SourceRunId): no FR-08, and the employee's attendance is his.
        await scope.TargetedAdjustmentAsync(employee, 500m, sourceId);
        scope.Db.ChangeTracker.Clear();
        var (created, correctionId, error) = await scope.Mediator.Send(new CreateCorrectionRunCommand(sourceId, source.RunDate));
        Assert.True(created, error);
        Assert.True((await scope.Mediator.Send(new RunPayrollFraudScanCommand(correctionId.Value))).Success);
        var correctionAlerts = await scope.Db.FraudAlerts.AsNoTracking().Where(a => a.PayrollRunId == correctionId && a.EmployeeId == employee).ToListAsync();
        Assert.DoesNotContain(correctionAlerts, a => a.RuleId is FraudRuleIds.GhostEmployee or FraudRuleIds.MissingSourceRecord);
    }

    [Fact]
    public async Task The_fraud_inputs_read_real_facts_and_raise_FR_01_FR_05_and_FR_07_and_a_correction_has_its_source()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await using var request = scope.NewRequest();
        var engine = request.ServiceProvider.GetRequiredService<FraudAnomalyEngine>();
        var employee = Guid.NewGuid();
        var run = new PayrollRun { Id = Guid.NewGuid(), RunDate = new DateTime(2030, 4, 1, 0, 0, 0, DateTimeKind.Utc), RunType = PayrollRunType.Regular };
        var line = new SalaryLine { EmployeeId = employee, NetPay = 9_000m };
        var facts = new FraudScanFacts(
            new HashSet<Guid> { employee },
            HireDates: new Dictionary<Guid, DateTime> { [employee] = run.RunDate },
            AttendanceByEmployee: new Dictionary<Guid, bool> { [employee] = false },
            ExpectedNetPay: new Dictionary<Guid, decimal> { [employee] = 2_000m });

        var input = Assert.Single(FraudScanInputBuilder.FromRun(run, [line], facts, run.RunDate));
        Assert.Equal(((bool?)false, (DateTime?)run.RunDate, (decimal?)2_000m, (bool?)true), (input.HasAttendance, input.HireDate, input.ExpectedNetPay, input.HasSourceRecord));
        var raised = engine.Scan(run.Id, [input], run.RunDate).Alerts.Select(a => a.RuleId).Order().ToList();
        Assert.Equal([FraudRuleIds.GhostEmployee, FraudRuleIds.AnomalousCompensation, FraudRuleIds.SameDayHireAndPay], raised);

        // No fact is no finding: no receipt, no earlier posted run, a hire date long before the run.
        var plain = FraudScanInputBuilder.FromRun(run, [line], new FraudScanFacts(new HashSet<Guid> { employee },
            HireDates: new Dictionary<Guid, DateTime> { [employee] = run.RunDate.AddYears(-3) }), run.RunDate);
        Assert.Equal(((bool?)null, (decimal?)null), (plain[0].HasAttendance, plain[0].ExpectedNetPay));
        Assert.Empty(engine.Scan(run.Id, plain, run.RunDate).Alerts);

        // A correction's lines carry their source run: FR-08 is not raised for want of an input snapshot.
        var correction = new PayrollRun { Id = Guid.NewGuid(), RunDate = run.RunDate, RunType = PayrollRunType.Correction, SourceRunId = run.Id };
        var corrected = FraudScanInputBuilder.FromRun(correction, [line], new FraudScanFacts(new HashSet<Guid>()), run.RunDate);
        Assert.True(corrected[0].HasSourceRecord);
        Assert.Empty(engine.Scan(correction.Id, corrected, run.RunDate).Alerts);
    }
}
