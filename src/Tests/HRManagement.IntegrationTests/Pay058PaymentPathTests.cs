using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PartialDisbursement;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Features.PayrollRuns.Reversal;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-58 (§1, P-5): money leaves by one path. The posting makes the batch and one order per line (an employee with no pinned account is
/// held <c>NO-ACCOUNT</c>); a funding decision comes after the posting only, on the batch; <c>process</c> sends, by a person who made,
/// approved and funded none of it; the batch's orders plus its arrears are the run's net at every transition; and a reversal of a run
/// whose funded batch never left voids the batch, cancels its orders and closes its arrears.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Pay058PaymentPathTests
{
    private static readonly Guid Decider = Guid.Parse("58000000-0000-4000-8000-0000000000d1");
    private static readonly Guid Sender = Pay056RunScope.Poster;

    private static async Task<Guid> AccountAsync(Pay056RunScope scope, Guid employee)
    {
        var account = Pay055TestAccounts.Active(employee);
        scope.Db.Add(account);
        await scope.Db.SaveChangesAsync();
        return account.Id;
    }

    private static async Task RulesAsync(Pay056RunScope scope)
    {
        scope.Db.DisbursementPriorityRules.Add(new DisbursementPriorityRule
        {
            RuleSetId = Guid.NewGuid(),
            Category = "RegularPayroll",
            Priority = 1,
            LegalSource = "تعميم الخزينة",
            IsActive = true,
        });
        await scope.Db.SaveChangesAsync();
    }

    private static async Task<PaymentBatch> BatchAsync(Pay056RunScope scope, Guid runId)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.PaymentBatches.AsNoTracking().SingleAsync(b => b.PayrollRunId == runId);
    }

    private static async Task<List<PaymentOrder>> OrdersAsync(Pay056RunScope scope, Guid batchId)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.PaymentOrders.AsNoTracking().Where(o => o.PaymentBatchId == batchId).OrderBy(o => o.EmployeeId).ToListAsync();
    }

    [Fact]
    public async Task The_posting_makes_the_batch_and_one_order_per_line_and_holds_a_line_without_an_account()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var paid = await scope.EmployeeAsync();
        var accountless = await scope.EmployeeAsync();
        var account = await AccountAsync(scope, paid);

        var runId = await scope.PostedRunAsync();

        var run = await scope.RunAsync(runId);
        var batch = await BatchAsync(scope, runId);
        Assert.Equal((PaymentBatchStatus.Pending, PaymentBatchSource.PayrollRunPosted, run.TotalNetPay), (batch.Status, batch.Source, batch.TotalAmount));
        var orders = await OrdersAsync(scope, batch.Id);
        var lines = await scope.LinesAsync(runId);
        Assert.Equal(lines.Count, orders.Count);
        Assert.Equal(run.TotalNetPay, orders.Sum(o => o.Amount));
        var paidOrder = orders.Single(o => o.EmployeeId == paid);
        Assert.Equal((PaymentOrderStatus.Draft, (string?)null), (paidOrder.Status, paidOrder.HoldReason));
        Assert.Equal(Pay055TestAccounts.Iban(paid), paidOrder.BeneficiaryIban);
        Assert.Equal(account, lines.Single(l => l.EmployeeId == paid).BankAccountId);
        var held = orders.Single(o => o.EmployeeId == accountless);
        Assert.Equal((PaymentOrderStatus.Held, PaymentOrderHoldReasons.NoAccount), (held.Status, held.HoldReason));
        Assert.Null(held.BeneficiaryIban);

        // The sending: the held employee's order stays out; the other is submitted. Nothing is sent twice.
        var sent = await scope.Mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Sender));
        Assert.True(sent.Success, sent.Error);
        orders = await OrdersAsync(scope, batch.Id);
        Assert.Equal(PaymentOrderStatus.Submitted, orders.Single(o => o.EmployeeId == paid).Status);
        Assert.Equal(PaymentOrderStatus.Held, orders.Single(o => o.EmployeeId == accountless).Status);
        Assert.Equal(PaymentBatchStatus.Submitted, (await BatchAsync(scope, runId)).Status);
        Assert.Equal(RunActOutcome.Conflict, (await scope.Mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Sender))).Outcome);
        Assert.Equal(PaymentBatchMessages.NothingReleased, (await scope.Mediator.Send(new SubmitHeldPaymentOrdersCommand(batch.Id, Sender))).Error);
    }

    [Fact]
    public async Task A_funding_decision_on_a_run_that_is_not_posted_is_refused_and_changes_nothing()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        await RulesAsync(scope);
        var runId = await scope.ApprovedRunAsync();

        var refused = await scope.Mediator.Send(new CreateRunFundingDecisionCommand(runId, null, 0.6m, null, Decider));

        Assert.Equal((RunActOutcome.Invalid, FundingDecisionMessages.AfterPostingOnly), (refused.Outcome, refused.Error));
        Assert.Empty(await scope.Db.FundingDecisions.AsNoTracking().ToListAsync());
        Assert.Empty(await scope.Db.PaymentBatches.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task The_sender_made_approved_and_funded_none_of_it()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        await AccountAsync(scope, employee);
        await RulesAsync(scope);
        var runId = await scope.PostedRunAsync();
        var batch = await BatchAsync(scope, runId);
        var funded = await scope.Mediator.Send(new CreateFundingDecisionCommand(batch.Id, null, 1m, null, Decider));
        Assert.True(funded.Success, funded.Error);

        Assert.Equal(PaymentBatchMessages.ExecutorIsApprover, (await scope.Mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Pay056RunScope.Approver))).Error);
        Assert.Equal(PaymentBatchMessages.ExecutorIsFundingDecider, (await scope.Mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Decider))).Error);
        var run = await scope.RunAsync(runId);
        if (run.CreatedBy is Guid creator)
            Assert.Equal(PaymentBatchMessages.ExecutorIsCreator, (await scope.Mediator.Send(new ProcessPaymentBatchCommand(batch.Id, creator))).Error);
        Assert.Equal(PaymentBatchStatus.Pending, (await BatchAsync(scope, runId)).Status);
        Assert.True((await scope.Mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Sender))).Success);
    }

    [Fact]
    public async Task The_sum_rule_refuses_a_sending_whose_orders_no_longer_make_the_runs_net()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        await AccountAsync(scope, employee);
        var runId = await scope.PostedRunAsync();
        var batch = await BatchAsync(scope, runId);

        // Tamper: an order's amount moves after the posting.
        var order = await scope.Db.PaymentOrders.SingleAsync(o => o.PaymentBatchId == batch.Id);
        order.Amount += 1m;
        await scope.Db.SaveChangesAsync();

        var refused = await scope.Mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Sender));

        Assert.Equal(PaymentBatchLedger.Broken, refused.Error);
        Assert.Equal(PaymentBatchStatus.Pending, (await BatchAsync(scope, runId)).Status);
        Assert.Equal(PaymentOrderStatus.Draft, (await OrdersAsync(scope, batch.Id)).Single().Status);

        // The rule itself: orders that are not cancelled, plus the funding's arrears, equal the payable net.
        var probe = new PaymentBatch { ArrearsAmount = 40m };
        Assert.Null(PaymentBatchLedger.Check(probe, [new PaymentOrder { Amount = 60m }, new PaymentOrder { Amount = 5m, Status = PaymentOrderStatus.Cancelled }], 100m));
        Assert.Equal(PaymentBatchLedger.Broken, PaymentBatchLedger.Check(probe, [new PaymentOrder { Amount = 61m }], 100m));
    }

    [Fact]
    public async Task A_partial_funding_pays_the_orders_in_the_order_of_their_employees_so_the_same_decision_funds_the_same_people()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employees = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var employee = await scope.EmployeeAsync();
            await AccountAsync(scope, employee);
            employees.Add(employee);
        }

        await RulesAsync(scope);
        var runId = await scope.PostedRunAsync();
        var batch = await BatchAsync(scope, runId);

        var funded = await scope.Mediator.Send(new CreateFundingDecisionCommand(batch.Id, null, 0.5m, null, Decider));

        Assert.True(funded.Success, funded.Error);
        var orders = await OrdersAsync(scope, batch.Id);
        var nets = (await scope.LinesAsync(runId)).ToDictionary(l => l.EmployeeId, l => l.NetPay);
        // Equal claims of one priority: the lowest employee id is paid in full, the highest gets nothing (its order is cancelled).
        Assert.Equal(nets[orders[0].EmployeeId], orders[0].Amount);
        Assert.Equal(PaymentOrderStatus.Cancelled, orders[2].Status);
        Assert.InRange(orders.Where(o => o.Status != PaymentOrderStatus.Cancelled).Sum(o => o.Amount), batch.TotalAmount * 0.5m - 0.01m, batch.TotalAmount * 0.5m + 0.01m);
    }

    [Fact]
    public async Task A_reversal_of_a_run_whose_funded_batch_never_left_voids_it_and_closes_its_arrears()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var first = await scope.EmployeeAsync();
        var second = await scope.EmployeeAsync();
        await AccountAsync(scope, first);
        await AccountAsync(scope, second);
        await RulesAsync(scope);
        var runId = await scope.PostedRunAsync();
        var batch = await BatchAsync(scope, runId);
        var funded = await scope.Mediator.Send(new CreateFundingDecisionCommand(batch.Id, null, 0.5m, null, Decider));
        Assert.True(funded.Success, funded.Error);
        Assert.NotEmpty(await scope.Db.ArrearsLedgerEntries.AsNoTracking().Where(a => a.PayrollRunId == runId).ToListAsync());

        var requested = await scope.Mediator.Send(new RequestPayrollReversalCommand(runId, Pay056RunScope.Reverser, "خطأ في الفترة"));
        Assert.True(requested.Success, requested.Error);
        var decided = await scope.Mediator.Send(new DecidePayrollReversalCommand(requested.Id!.Value, Pay056RunScope.OtherApprover, true, "موافق"));
        Assert.True(decided.Success, decided.Error);

        Assert.Equal(PayrollRunStatus.Reversed, (await scope.RunAsync(runId)).Status);
        Assert.Equal(PaymentBatchStatus.Voided, (await BatchAsync(scope, runId)).Status);
        Assert.All(await OrdersAsync(scope, batch.Id), o => Assert.Equal(PaymentOrderStatus.Cancelled, o.Status));
        var arrears = await scope.Db.ArrearsLedgerEntries.AsNoTracking().Where(a => a.PayrollRunId == runId).ToListAsync();
        Assert.All(arrears, a =>
        {
            Assert.Equal(ArrearsLedgerStatus.Voided, a.Status);
            Assert.Contains("خطأ في الفترة", a.Reason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_run_whose_funded_batch_left_is_still_not_reversed()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        await AccountAsync(scope, employee);
        await RulesAsync(scope);
        var runId = await scope.PostedRunAsync();
        var batch = await BatchAsync(scope, runId);
        Assert.True((await scope.Mediator.Send(new CreateFundingDecisionCommand(batch.Id, null, 0.5m, null, Decider))).Success);
        Assert.True((await scope.Mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Sender))).Success);

        var requested = await scope.Mediator.Send(new RequestPayrollReversalCommand(runId, Pay056RunScope.Reverser, "خطأ"));
        Assert.True(requested.Success, requested.Error);
        var decided = await scope.Mediator.Send(new DecidePayrollReversalCommand(requested.Id!.Value, Pay056RunScope.OtherApprover, true, "موافق"));

        Assert.Equal(PayrollReversalMessages.MoneyHasLeft, decided.Error);
        Assert.Equal(PayrollRunStatus.Posted, (await scope.RunAsync(runId)).Status);
        Assert.All(await scope.Db.ArrearsLedgerEntries.AsNoTracking().Where(a => a.PayrollRunId == runId).ToListAsync(),
            a => Assert.NotEqual(ArrearsLedgerStatus.Voided, a.Status));
    }
}
