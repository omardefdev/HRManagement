using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Insurance;
using HRManagement.Integration.Abstractions.Payroll;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.BankAccounts;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Integration;
using Payroll.Infrastructure.Services;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-55: the plan's three proofs on PostgreSQL. (1) A run built by the product's own commands pins each employee's account at its
/// snapshot, and its payment orders and its treasury file carry the pinned account, not the current one. (2) Two verifications at once:
/// one wins and the other is a conflict (the filtered unique index and <c>xmin</c>). (3) <c>IEmployeeCreated</c> then
/// <c>IContractActivated</c>, and the reverse order, each leave an active pay profile with the unit and the regime.
/// </summary>
[Collection("PostgreSql")]
public sealed partial class Pay055BankAccountPostgreSqlTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private static readonly Guid Processor = Guid.Parse("55000000-0000-4000-8000-0000000000c1");
    private static readonly Guid Approver = Guid.Parse("55000000-0000-4000-8000-0000000000c2");
    private static readonly Guid OtherApprover = Guid.Parse("55000000-0000-4000-8000-0000000000c3");

    private static Guid Employee(int i) => Guid.Parse($"b0000001-0000-4000-8000-{i:D12}");

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>An account as two people leave it: registered by the processor, verified by an approver with a reference.</summary>
    private static async Task<Guid> RegisterAndVerifyAsync(IMediator mediator, Guid employee, int variant, Guid? verifier = null)
    {
        var registered = await mediator.Send(new RequestBankAccountCommand(
            employee, BankAccountKind.Iban, Pay055TestAccounts.Iban(employee, variant), null, "مصرف الاختبار", null, Processor, null));
        Assert.True(registered.Success, registered.Error);
        var verified = await mediator.Send(new VerifyBankAccountCommand(
            employee, registered.AccountId!.Value, $"كتاب المصرف {variant + 1}", verifier ?? Approver, null));
        Assert.True(verified.Success, verified.Error);
        return registered.AccountId.Value;
    }

    private static async Task<Guid> CalculateAsync(PayrollDbContext db, IMediator mediator, DateTime periodStart, DateTime periodEnd)
    {
        var (created, runId, createError) = await mediator.Send(new CreatePayrollRunCommand(periodStart, periodEnd, periodEnd));
        Assert.True(created, createError);
        var (ok, _, calculateError) = await mediator.Send(new CalculatePayrollRunCommand(runId!.Value));
        Assert.True(ok, calculateError);
        db.ChangeTracker.Clear();
        return runId.Value;
    }

    private static async Task<Dictionary<Guid, Guid?>> PinnedOnLinesAsync(PayrollDbContext db, Guid runId) =>
        await db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == runId).ToDictionaryAsync(l => l.EmployeeId, l => l.BankAccountId);

    private static async Task<Dictionary<Guid, Guid?>> PinnedOnSnapshotsAsync(PayrollDbContext db, Guid runId) =>
        await db.PayrollRunInputSnapshots.AsNoTracking()
            .Where(s => s.Job.PayrollRunId == runId)
            .ToDictionaryAsync(s => s.EmployeeId, s => s.BankAccountId);

    /// <summary>The treasury file of a run, as the gateway receives it. The export takes a posted run: the test posts it directly.</summary>
    private static async Task<PayrollDisbursementRequest> ExportTreasuryAsync(PayrollDbContext db, Guid runId, IReadOnlyDictionary<Guid, string> names)
    {
        await db.PayrollRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, PayrollRunStatus.Posted));
        db.ChangeTracker.Clear();

        PayrollDisbursementRequest? captured = null;
        var gateway = new Mock<IPayrollDisbursementGateway>();
        gateway.Setup(g => g.SubmitAsync(It.IsAny<PayrollDisbursementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PayrollDisbursementRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new IntegrationResult(true));
        var reporter = new Mock<ISocialInsuranceReporter>();
        reporter.Setup(r => r.SubmitAsync(It.IsAny<SocialInsuranceReportRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IntegrationResult(true));
        var lookup = new Mock<IEmployeeLookup>();
        lookup.Setup(l => l.GetEmployeeDisplayNamesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(names);

        await new PayrollExternalExportService(db, lookup.Object, gateway.Object, reporter.Object).ExportRunAsync(runId);
        db.ChangeTracker.Clear();
        return captured!;
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_run_pins_each_account_at_its_snapshot_and_its_orders_and_treasury_file_carry_the_pinned_account_not_the_current_one()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(fixture);
        await using var disposeScope = scope;
        await using var disposeRoot = root;
        await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
        await PayrollPostgresTestSupport.SeedEmployeesAsync(db, 3);
        var (e1, e2, e3) = (Employee(1), Employee(2), Employee(3));
        var names = new Dictionary<Guid, string> { [e1] = "أحمد الخطيب", [e2] = "سلمى الحسن", [e3] = "خالد العلي" };

        // Every employee has an account two people left in force.
        var a1 = await RegisterAndVerifyAsync(mediator, e1, 0);
        var a2 = await RegisterAndVerifyAsync(mediator, e2, 0);
        var a3 = await RegisterAndVerifyAsync(mediator, e3, 0);

        // October is calculated: the snapshot pins each account, and the line copies it.
        var october = await CalculateAsync(db, mediator, Utc(2026, 10, 1), Utc(2026, 10, 31));
        var expected = new Dictionary<Guid, Guid?> { [e1] = a1, [e2] = a2, [e3] = a3 };
        Assert.Equal(expected, await PinnedOnSnapshotsAsync(db, october));
        Assert.Equal(expected, await PinnedOnLinesAsync(db, october));
        var netBefore = await db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == october).OrderBy(l => l.EmployeeId).Select(l => l.NetPay).ToListAsync();

        // After the calculation: e1's replacement is verified (the first account is retired by it), and a person retires e2's account.
        var b1 = await RegisterAndVerifyAsync(mediator, e1, 1, OtherApprover);
        var retired = await mediator.Send(new RetireBankAccountCommand(e2, a2, "أغلق الموظف حسابه", Approver, null));
        Assert.True(retired.Success, retired.Error);
        Assert.Equal(1, retired.HoldsPlaced);
        db.ChangeTracker.Clear();

        // The run's lines and snapshots did not move: a change of account after the snapshot applies from the next cycle.
        Assert.Equal(expected, await PinnedOnLinesAsync(db, october));
        Assert.Equal(expected, await PinnedOnSnapshotsAsync(db, october));
        Assert.Equal(netBefore, await db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == october).OrderBy(l => l.EmployeeId).Select(l => l.NetPay).ToListAsync());
        Assert.Equal(BankAccountStatus.Retired, (await db.EmployeeBankAccounts.AsNoTracking().SingleAsync(a => a.Id == a1)).Status);
        Assert.Equal(BankAccountStatus.Active, (await db.EmployeeBankAccounts.AsNoTracking().SingleAsync(a => a.Id == b1)).Status);

        // Both retirements put the employee behind a hold in that run: by the replacement's verification, and by the person's act.
        var holds = await db.PaymentHolds.AsNoTracking().Where(h => h.PayrollRunId == october).OrderBy(h => h.EmployeeId).ToListAsync();
        Assert.Equal(new[] { e1, e2 }, holds.Select(h => h.EmployeeId));
        Assert.All(holds, h =>
        {
            Assert.StartsWith(EmployeeBankAccount.RetiredHoldRule, h.Reason);
            Assert.Equal((PaymentHoldType.Manual, PaymentHoldStatus.Active), (h.HoldType, h.Status));
            Assert.DoesNotContain("SY", h.Reason, StringComparison.Ordinal);
        });

        // The readiness report: three lines with a pinned account, none without, two whose pinned account was retired since.
        var readiness = (await mediator.Send(new GetRunBankReadinessQuery(october)))!;
        Assert.Equal((3, 3, 0), (readiness.Lines, readiness.LinesWithAccount, readiness.LinesWithoutAccount));
        Assert.Empty(readiness.EmployeesWithoutAccount);
        Assert.Equal(new[] { e1, e2 }, readiness.EmployeesWithRetiredAccount);

        // The payment orders carry the pinned account of each line: e1's first account, not the one in force now.
        var total = await db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == october).SumAsync(l => l.NetPay);
        var batch = new PaymentBatch { PayrollRunId = october, TotalAmount = total, Status = PaymentBatchStatus.Pending, BatchKind = PaymentBatchKind.Disbursement };
        db.PaymentBatches.Add(batch);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var orders = await scope.ServiceProvider.GetRequiredService<PaymentOrderService>().EnsureOrdersForBatchAsync(batch.Id);
        Assert.Equal(
            new Dictionary<Guid, string?> { [e1] = Pay055TestAccounts.Iban(e1, 0), [e2] = Pay055TestAccounts.Iban(e2, 0), [e3] = Pay055TestAccounts.Iban(e3, 0) },
            orders.ToDictionary(o => o.EmployeeId, o => o.BeneficiaryIban));
        db.ChangeTracker.Clear();

        // The treasury file of the posted run carries the pinned accounts too.
        var file = await ExportTreasuryAsync(db, october, names);
        Assert.Equal(
            new Dictionary<Guid, string?> { [e1] = Pay055TestAccounts.Iban(e1, 0), [e2] = Pay055TestAccounts.Iban(e2, 0), [e3] = Pay055TestAccounts.Iban(e3, 0) },
            file.Lines.ToDictionary(l => l.EmployeeId, l => l.BankAccount));
        Assert.DoesNotContain(file.Lines, l => l.BankAccount == Pay055TestAccounts.Iban(e1, 1));

        // November: a new employee with no account joins. The run takes e1's new account; e2 and the new employee have none.
        var e4 = Guid.Parse("b0000001-0000-4000-8000-000000000004");
        db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(e4, 90_000m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var november = await CalculateAsync(db, mediator, Utc(2026, 11, 1), Utc(2026, 11, 30));
        Assert.Equal(new Dictionary<Guid, Guid?> { [e1] = b1, [e2] = null, [e3] = a3, [e4] = null }, await PinnedOnLinesAsync(db, november));
        var next = (await mediator.Send(new GetRunBankReadinessQuery(november)))!;
        Assert.Equal((4, 2, 2), (next.Lines, next.LinesWithAccount, next.LinesWithoutAccount));
        Assert.Equal(new[] { e2, e4 }.Order(), next.EmployeesWithoutAccount);
        Assert.Empty(next.EmployeesWithRetiredAccount);

        // A line without an account blocked neither the calculation nor the report; the treasury file is refused, and the refusal
        // lists the employees: their ids for an API, their names (never an id, never a number) for a screen.
        var refusal = await Assert.ThrowsAsync<MissingPinnedBankAccountsException>(() => ExportTreasuryAsync(db, november, names));
        Assert.Equal(new[] { e2, e4 }.Order(), refusal.EmployeeIds.Order());
        Assert.Contains("سلمى الحسن", refusal.Message, StringComparison.Ordinal);        // e2 has a name in the directory
        Assert.Contains("و1 آخرون", refusal.Message, StringComparison.Ordinal);          // e4 has none: counted, not shown by an id
        Assert.DoesNotContain(e4.ToString("D"), refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(e2.ToString("D"), refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SY", refusal.Message, StringComparison.Ordinal);
        output.WriteLine(refusal.Message);
    }

    private static async Task<(bool Present, bool Verified)> DsarAsync(PayrollDbContext db, Guid employee)
    {
        db.ChangeTracker.Clear();
        var result = await new Payroll.Web.Controllers.DsarInternalController(db).GetSubject(employee, CancellationToken.None);
        var body = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result).Value!;
        var json = System.Text.Json.JsonSerializer.SerializeToElement(body);
        return (json.GetProperty("bankAccountPresent").GetBoolean(), json.GetProperty("bankAccountVerified").GetBoolean());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_dsar_answer_says_present_and_verified_from_the_account_entity()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(fixture);
        await using var disposeScope = scope;
        await using var disposeRoot = root;
        await PayrollPostgresTestSupport.SeedEmployeesAsync(db, 2);
        var (e1, e2) = (Employee(1), Employee(2));
        Assert.Equal((false, false), await DsarAsync(db, e1));

        // Registered, waiting for a second person: present, not verified.
        var registered = await mediator.Send(new RequestBankAccountCommand(
            e1, BankAccountKind.Iban, Pay055TestAccounts.Iban(e1), null, "مصرف الاختبار", null, Processor, null));
        Assert.True(registered.Success, registered.Error);
        Assert.Equal((true, false), await DsarAsync(db, e1));

        Assert.True((await mediator.Send(new VerifyBankAccountCommand(e1, registered.AccountId!.Value, "كتاب المصرف 7", Approver, null))).Success);
        Assert.Equal((true, true), await DsarAsync(db, e1));
        Assert.Equal((false, false), await DsarAsync(db, e2));     // another employee's answer does not move

        Assert.True((await mediator.Send(new RetireBankAccountCommand(e1, registered.AccountId.Value, "أغلق الموظف حسابه", Approver, null))).Success);
        Assert.Equal((false, false), await DsarAsync(db, e1));
    }
}
