using System.Text.Json;
using HRManagement.TestSupport;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.BankAccounts;
using Payroll.Application.Frd;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;
using Payroll.Infrastructure.Repositories;
using Payroll.Infrastructure.Services;
using H = HRManagement.UnitTests.Pay055BankAccountHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-55 (§2, D-PAY-15): the account a run pinned. The line carries the snapshot's account and its amounts do not depend on it;
/// payment orders, FR-04 and the readiness report read the pinned account, never the pay profile and never «the current account». (The
/// DSAR answer is proved on PostgreSQL, in <c>Pay055BankAccountPostgreSqlTests</c>.)
/// </summary>
public sealed class Pay055PinningTests
{
    private static async Task<H> SeededAsync() => await new H().SeedAsync();

    // --- the line ---

    private static PayrollEmployeeCalculationPackage Build(Guid? bankAccountId)
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            RunDate = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc),
        };
        var profile = H.Profile(H.Employee, H.Unit);
        return PayrollEmployeeCalculationUnit.Build(
            PayrollEngine.CreateDefaultPipeline(), run, profile, null, config, new HashSet<DateOnly>(),
            [], [], [], [], [], bankAccountId: bankAccountId);
    }

    [Fact]
    public void The_line_carries_the_snapshots_account_and_its_amounts_do_not_depend_on_it()
    {
        var account = Guid.NewGuid();

        var without = Build(null);
        var with = Build(account);

        Assert.Null(without.Line.BankAccountId);
        Assert.Equal(account, with.Line.BankAccountId);
        // The account is not an input of the amount: the same line, to the last component.
        Assert.True(with.Line.NetPay > 0);
        Assert.Equal(
            (without.Line.GrossPay, without.Line.TotalDeductions, without.Line.NetPay, without.Line.PayComponents),
            (with.Line.GrossPay, with.Line.TotalDeductions, with.Line.NetPay, with.Line.PayComponents));
        Assert.Equal(
            without.Components.Select(c => (c.ComponentType, c.ComponentName, c.Amount)),
            with.Components.Select(c => (c.ComponentType, c.ComponentName, c.Amount)));
    }

    [Fact]
    public void Neither_the_profile_version_nor_the_input_fingerprint_knows_an_account()
    {
        // The two tokens a snapshot keeps to detect a changed input are computed from the profile and from the pay inputs only: the
        // determinism tests and AC-21 do not move with an account.
        var parameters = typeof(PayrollInputFingerprint).GetMethod(nameof(PayrollInputFingerprint.Compute))!.GetParameters();
        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(EmployeeBankAccount) || p.Name!.Contains("account", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(PayrollEmployeeCalculationUnit).GetMethod(nameof(PayrollEmployeeCalculationUnit.ComputeProfileVersion))!.GetParameters(),
            p => p.ParameterType != typeof(EmployeePayProfile));
    }

    // --- what a snapshot pins ---

    [Fact]
    public async Task A_snapshot_pins_the_account_in_force_and_nothing_for_an_employee_without_one()
    {
        var h = await SeededAsync();
        var active = await h.ActiveAccountAsync(H.Iban1);
        Assert.True((await h.RegisterAsync(H.Iban2, employee: H.OtherEmployee)).Success);   // pending only: not pinned

        await using var db = h.Db();
        var pinned = await BankAccountPinning.ActiveAccountsAsync(new PayrollRepository(db), CancellationToken.None);

        Assert.Equal(new Dictionary<Guid, Guid> { [H.Employee] = active }, pinned);
    }

    [Fact]
    public async Task A_rejected_or_retired_account_is_never_pinned()
    {
        var h = await SeededAsync();
        var retired = await h.ActiveAccountAsync(H.Iban1);
        Assert.True((await h.RetireAsync(retired)).Success);
        var rejected = (await h.RegisterAsync(H.Iban2)).AccountId!.Value;
        Assert.True((await h.RejectAsync(rejected)).Success);

        await using var db = h.Db();
        Assert.Empty(await BankAccountPinning.ActiveAccountsAsync(new PayrollRepository(db), CancellationToken.None));
    }

    // --- payment orders ---

    [Fact]
    public async Task A_payment_order_takes_the_lines_pinned_account_even_after_a_replacement_is_verified()
    {
        var h = await SeededAsync();
        var first = await h.ActiveAccountAsync(H.Iban1);
        var (run, line) = await h.RunLineAsync(first);
        await h.RunLineAsync(accountId: null, employee: H.OtherEmployee, month: 10);   // another run: not this batch
        // After the calculation the employee's account changes.
        var replacement = await h.RegisterAsync(H.Iban2);
        Assert.True((await h.VerifyAsync(replacement.AccountId!.Value)).Success);
        Guid batch;
        await using (var seed = h.Db())
        {
            var created = new PaymentBatch { PayrollRunId = run, TotalAmount = 4000m, Status = PaymentBatchStatus.Pending, BatchKind = PaymentBatchKind.Disbursement };
            seed.Add(created);
            await seed.SaveChangesAsync();
            batch = created.Id;
        }

        await using var db = h.Db();
        var orders = await new PaymentOrderService(db).EnsureOrdersForBatchAsync(batch);

        var order = Assert.Single(orders);
        Assert.Equal(line, order.SalaryLineId);
        Assert.Equal(H.Iban1, order.BeneficiaryIban);          // the pinned account, not the one in force now
        Assert.NotEqual(H.Iban2, order.BeneficiaryIban);
    }

    [Fact]
    public async Task A_line_without_a_pinned_account_gets_an_order_without_a_destination_whatever_the_employee_has_now()
    {
        var h = await SeededAsync();
        var (run, _) = await h.RunLineAsync(accountId: null);
        await h.ActiveAccountAsync(H.Iban1);                     // registered and verified after the calculation
        Guid batch;
        await using (var seed = h.Db())
        {
            var created = new PaymentBatch { PayrollRunId = run, TotalAmount = 4000m, Status = PaymentBatchStatus.Pending, BatchKind = PaymentBatchKind.Disbursement };
            seed.Add(created);
            await seed.SaveChangesAsync();
            batch = created.Id;
        }

        await using var db = h.Db();
        var order = Assert.Single(await new PaymentOrderService(db).EnsureOrdersForBatchAsync(batch));

        Assert.Null(order.BeneficiaryIban);
    }

    // --- FR-04 ---

    private static FraudAnomalyEngine FraudEngine()
    {
        var json = global::System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "frd", "anomaly-rules.json"));
        var document = JsonSerializer.Deserialize<FrdAnomalyRulesDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new FraudAnomalyEngine(new Rules(document));
    }

    private sealed class Rules(FrdAnomalyRulesDocument document) : IFrdAnomalyRulesProvider
    {
        public FrdAnomalyRulesDocument Document => document;

        public FrdDetectionRule? FindRule(string ruleId) =>
            document.DetectionRules.FirstOrDefault(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));

        public bool HoldOnProofOfLifeExpiry => document.ProofOfLife?.HoldOnExpiry ?? true;

        public bool SingleOperatorReleaseBlocked => document.PaymentHold?.SingleOperatorReleaseBlocked ?? true;
    }

    [Fact]
    public async Task Fr04_sees_one_account_number_pinned_by_two_employees_of_a_run()
    {
        var h = await SeededAsync();
        // Two employees whose verified accounts carry the same number (each registered and verified by two people).
        var mine = await h.ActiveAccountAsync(H.Iban1);
        var theirs = await h.ActiveAccountAsync(H.Iban1, employee: H.OtherEmployee);
        var run = new PayrollRun { RunDate = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc) };
        var lines = new List<SalaryLine>
        {
            new() { PayrollRunId = run.Id, EmployeeId = H.Employee, NetPay = 4000m, BankAccountId = mine },
            new() { PayrollRunId = run.Id, EmployeeId = H.OtherEmployee, NetPay = 4100m, BankAccountId = theirs },
            new() { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid(), NetPay = 3900m, BankAccountId = null },
        };
        await using var db = h.Db();
        var pinned = await BankAccountPinning.PinnedAccountsAsync(db.EmployeeBankAccounts, lines, CancellationToken.None);
        var snapshotEmployees = lines.Select(l => l.EmployeeId).ToHashSet();

        var inputs = FraudScanInputBuilder.FromRun(
            run, lines, new FraudScanFacts(snapshotEmployees, pinned.ToDictionary(p => p.Key, p => p.Value.AccountNumber)), run.RunDate);
        var scan = FraudEngine().Scan(run.Id, inputs, asOfUtc: run.RunDate);

        Assert.Equal(new string?[] { H.Iban1, H.Iban1, null }, inputs.Select(i => i.Iban));
        var shared = scan.Alerts.Where(a => a.RuleId == FraudRuleIds.SharedIban).ToList();
        Assert.Equal(new[] { H.Employee, H.OtherEmployee }.Order(), shared.Select(a => a.EmployeeId).Order());
        // The alert names no number.
        Assert.All(shared, a => Assert.DoesNotContain(H.Iban1, a.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Fr04_reads_the_pinned_account_not_the_one_in_force_now()
    {
        var h = await SeededAsync();
        var mine = await h.ActiveAccountAsync(H.Iban1);
        var theirs = await h.ActiveAccountAsync(H.Iban2, employee: H.OtherEmployee);
        var run = new PayrollRun { RunDate = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc) };
        var lines = new List<SalaryLine>
        {
            new() { PayrollRunId = run.Id, EmployeeId = H.Employee, NetPay = 4000m, BankAccountId = mine },
            new() { PayrollRunId = run.Id, EmployeeId = H.OtherEmployee, NetPay = 4100m, BankAccountId = theirs },
        };
        // After the calculation the second employee moves to the first one's number: this run's lines are not concerned.
        var moved = await h.RegisterAsync(H.Iban1, employee: H.OtherEmployee);
        Assert.True((await h.VerifyAsync(moved.AccountId!.Value, employee: H.OtherEmployee)).Success);

        await using var db = h.Db();
        var pinned = await BankAccountPinning.PinnedAccountsAsync(db.EmployeeBankAccounts, lines, CancellationToken.None);
        var inputs = FraudScanInputBuilder.FromRun(
            run, lines, new FraudScanFacts(lines.Select(l => l.EmployeeId).ToHashSet(), pinned.ToDictionary(p => p.Key, p => p.Value.AccountNumber)), run.RunDate);

        Assert.Equal(new[] { H.Iban1, H.Iban2 }, inputs.Select(i => i.Iban));
        Assert.DoesNotContain(FraudEngine().Scan(run.Id, inputs, asOfUtc: run.RunDate).Alerts, a => a.RuleId == FraudRuleIds.SharedIban);
    }

    // --- the readiness report ---

    [Fact]
    public async Task The_readiness_report_counts_the_pinned_lines_and_lists_the_employees_without_an_account()
    {
        var h = await SeededAsync();
        var account = await h.ActiveAccountAsync(H.Iban1);
        var (run, _) = await h.RunLineAsync(account);
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        await using (var seed = h.Db())
        {
            seed.AddRange(
                new SalaryLine { PayrollRunId = run, EmployeeId = second, NetPay = 1m },
                new SalaryLine { PayrollRunId = run, EmployeeId = third, NetPay = 1m },
                new SalaryLine { PayrollRunId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), NetPay = 1m });   // another run's line
            await seed.SaveChangesAsync();
        }

        await using var db = h.Db();
        var handler = new GetRunBankReadinessHandler(new PayrollRepository(db));
        var readiness = (await handler.Handle(new GetRunBankReadinessQuery(run), CancellationToken.None))!;

        Assert.Equal((run, 3, 1, 2), (readiness.PayrollRunId, readiness.Lines, readiness.LinesWithAccount, readiness.LinesWithoutAccount));
        Assert.Equal(new[] { second, third }.Order(), readiness.EmployeesWithoutAccount);
        Assert.Empty(readiness.EmployeesWithRetiredAccount);
        Assert.Null(await handler.Handle(new GetRunBankReadinessQuery(Guid.NewGuid()), CancellationToken.None));

        // The pinned account is retired after the calculation: the report says whose line is behind a hold.
        Assert.Equal(1, (await h.RetireAsync(account)).HoldsPlaced);
        await using var after = h.Db();
        var later = (await new GetRunBankReadinessHandler(new PayrollRepository(after)).Handle(new GetRunBankReadinessQuery(run), CancellationToken.None))!;
        Assert.Equal((3, 1, 2), (later.Lines, later.LinesWithAccount, later.LinesWithoutAccount));
        Assert.Equal(new[] { H.Employee }, later.EmployeesWithRetiredAccount);
        Assert.Equal(new[] { H.Employee }, later.EmployeesHeldForRetiredAccount);       // read from the holds: his hold is active

        // A person releases the hold: the report says the line is no longer held (it will be paid into the retired account).
        await using (var release = h.Db())
        {
            var hold = await release.PaymentHolds.SingleAsync();
            hold.Status = PaymentHoldStatus.Released;
            await release.SaveChangesAsync();
        }

        await using var afterRelease = h.Db();
        var released = (await new GetRunBankReadinessHandler(new PayrollRepository(afterRelease)).Handle(new GetRunBankReadinessQuery(run), CancellationToken.None))!;
        Assert.Equal(new[] { H.Employee }, released.EmployeesWithRetiredAccount);
        Assert.Empty(released.EmployeesHeldForRetiredAccount);
    }

    // --- a retired pinned account that no person released ---

    private static async Task<IReadOnlyList<Guid>> BlockedAsync(H h, Guid run)
    {
        await using var db = h.Db();
        var lines = await db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == run).ToListAsync();
        return await BankAccountPinning.RetiredAndNotReleasedAsync(
            db.EmployeeBankAccounts, db.PaymentHolds, db.PaymentOrders, db.PaymentBatches, run, lines, CancellationToken.None);
    }

    [Fact]
    public async Task A_retired_pinned_account_blocks_until_a_person_releases_its_hold()
    {
        var h = await SeededAsync();
        var account = await h.ActiveAccountAsync();
        var (run, _) = await h.RunLineAsync(account);
        Assert.Empty(await BlockedAsync(h, run));                                   // in force: nothing blocks

        Assert.Equal(1, (await h.RetireAsync(account)).HoldsPlaced);
        Assert.Equal(new[] { H.Employee }, await BlockedAsync(h, run));             // retired, hold active

        await using (var db = h.Db())
        {
            var hold = await db.PaymentHolds.SingleAsync();
            hold.Status = PaymentHoldStatus.Released;
            await db.SaveChangesAsync();
        }

        Assert.Empty(await BlockedAsync(h, run));                                   // a person decided: the pinned account is paid
    }

    [Fact]
    public async Task A_retired_pinned_account_with_no_hold_at_all_still_blocks()
    {
        // The safety net: a retirement that met neither a line nor a snapshot of the run placed no hold.
        var h = await SeededAsync();
        var account = await h.ActiveAccountAsync();
        Assert.Equal(0, (await h.RetireAsync(account)).HoldsPlaced);
        var (run, _) = await h.RunLineAsync(account);                                // the line is written afterwards, pinned to it

        Assert.Empty(await h.HoldsAsync());
        Assert.Equal(new[] { H.Employee }, await BlockedAsync(h, run));
    }

    [Fact]
    public async Task A_line_that_was_already_disbursed_when_its_account_was_retired_blocks_nothing()
    {
        var h = await SeededAsync();
        var account = await h.ActiveAccountAsync();
        var (run, _) = await h.RunLineAsync(account, order: PaymentOrderStatus.Settled);

        Assert.Equal(0, (await h.RetireAsync(account)).HoldsPlaced);

        Assert.Empty(await BlockedAsync(h, run));                                   // a later batch of the same run is not stopped by him
    }

    // --- naming the held employees (PLAN-55's rule; the treasury file's refusals that used it are gone with the file, PLAN-58) ---

    [Fact]
    public void A_list_of_people_shows_names_counts_the_rest_and_never_an_id()
    {
        var named = Enumerable.Range(1, PayrollEmployeeNames.NamesShown + 5).Select(i => (Id: Guid.NewGuid(), Name: $"موظف {i:D2}")).ToList();
        var unnamed = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var ids = named.Select(n => n.Id).Concat(unnamed).ToList();
        var names = named.ToDictionary(n => n.Id, n => n.Name);

        var listed = PayrollEmployeeNames.Listed(ids, names);

        Assert.Contains("موظف 01", listed, StringComparison.Ordinal);
        Assert.Contains($"موظف {PayrollEmployeeNames.NamesShown:D2}", listed, StringComparison.Ordinal);
        Assert.DoesNotContain($"موظف {PayrollEmployeeNames.NamesShown + 1:D2}", listed, StringComparison.Ordinal);
        Assert.Contains("و7 آخرون", listed, StringComparison.Ordinal);            // 5 named beyond the cap and 2 without a name
        Assert.All(ids, id => Assert.DoesNotContain(id.ToString("D"), listed, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("(عددهم 2)", PayrollEmployeeNames.Listed(unnamed, new Dictionary<Guid, string>()), StringComparison.Ordinal);
    }
}
