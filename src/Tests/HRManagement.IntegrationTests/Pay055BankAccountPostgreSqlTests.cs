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

    // --- the order of the two events (the plan's second proof) ---

    private static readonly Guid UnitOfTheNewHire = Guid.Parse("55000000-0000-4000-8000-0000000000a1");

    private static async Task<List<EmployeePayProfile>> ProfilesOfAsync(PayrollDbContext db, Guid employee)
    {
        db.ChangeTracker.Clear();
        return await db.EmployeePayProfiles.AsNoTracking().Where(p => p.EmployeeId == employee).OrderBy(p => p.CreatedAt).ToListAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Employee_created_then_contract_activated_leaves_an_active_profile_with_the_unit_and_the_regime()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(fixture);
        await using var disposeScope = scope;
        await using var disposeRoot = root;
        var events = root.GetRequiredService<RecordingPayProfileChangeEvents>();
        var employee = Guid.NewGuid();
        var contract = Guid.NewGuid();

        await mediator.Send(new Payroll.Application.Features.InboundEvents.HandleEmployeeCreatedCommand(employee, UnitOfTheNewHire, "law50"));
        var placeholder = Assert.Single(await ProfilesOfAsync(db, employee));
        Assert.Equal((false, 0m, (Guid?)UnitOfTheNewHire, "law50"), (placeholder.IsActive, placeholder.BaseSalary, placeholder.OrgUnitId, placeholder.RegimeCode));
        Assert.Equal(TimeSpan.Zero, placeholder.EffectiveFrom.TimeOfDay);       // a calendar date, not the instant of the event

        // The contract started before the employee's record reached Payroll: the placeholder holds no day, so nothing refuses it.
        var start = Utc(2026, 9, 1);
        db.ChangeTracker.Clear();
        await mediator.Send(new Payroll.Application.Features.InboundEvents.HandleContractActivatedCommand(contract, employee, "PERM", start, 250_000m, null));

        var profiles = await ProfilesOfAsync(db, employee);
        var active = Assert.Single(profiles, p => p.IsActive);
        Assert.Equal((UnitOfTheNewHire, "law50", 250_000m, start), (active.OrgUnitId, active.RegimeCode, active.BaseSalary, active.EffectiveFrom));
        var closed = Assert.Single(profiles, p => !p.IsActive);
        Assert.Equal((placeholder.Id, Utc(2026, 8, 31)), (closed.Id, closed.EffectiveTo));

        // One save wrote the version, the request row, the zero adjustment and the history; the event was staged once.
        var request = await db.PayProfileChangeRequests.AsNoTracking().SingleAsync(r => r.EmployeeId == employee);
        Assert.Equal(
            (PayProfileChangeState.Applied, PayProfileChangeSource.ContractActivated, contract.ToString("D"), active.Id, (Guid?)UnitOfTheNewHire),
            (request.State, request.Source, request.SourceReference, request.AppliedProfileId!.Value, request.OrgUnitId));
        Assert.Equal(1, await db.PayrollAdjustments.AsNoTracking().CountAsync(a => a.EmployeeId == employee && a.Amount == 0m));
        Assert.Equal(1, await db.EmployeePayProfileHistories.AsNoTracking().CountAsync(x => x.EmployeeId == employee));
        var applied = Assert.Single(events.Published.OfType<PayProfileChangeAppliedEvent>(), e => e.EmployeeId == employee);
        Assert.Equal((request.Id, active.Id, "ContractActivated"), (applied.RequestId, applied.NewProfileId, applied.Source));

        // The same message again: nothing changes (and the database's own index allows one active profile only).
        db.ChangeTracker.Clear();
        await mediator.Send(new Payroll.Application.Features.InboundEvents.HandleContractActivatedCommand(contract, employee, "PERM", start, 250_000m, null));
        Assert.Equal(2, (await ProfilesOfAsync(db, employee)).Count);
        Assert.Equal(1, await db.PayProfileChangeRequests.AsNoTracking().CountAsync(r => r.EmployeeId == employee));
        Assert.Equal(1, await db.PayrollAdjustments.AsNoTracking().CountAsync(a => a.EmployeeId == employee));
        Assert.Single(events.Published.OfType<PayProfileChangeAppliedEvent>(), e => e.EmployeeId == employee);

        // A second contract, later: the new version keeps the unit and the regime, and no day is in two versions.
        db.ChangeTracker.Clear();
        await mediator.Send(new Payroll.Application.Features.InboundEvents.HandleContractActivatedCommand(Guid.NewGuid(), employee, "PERM", Utc(2026, 12, 1), 270_000m, null));
        profiles = await ProfilesOfAsync(db, employee);
        var renewed = Assert.Single(profiles, p => p.IsActive);
        Assert.Equal((UnitOfTheNewHire, "law50", 270_000m, Utc(2026, 12, 1)), (renewed.OrgUnitId, renewed.RegimeCode, renewed.BaseSalary, renewed.EffectiveFrom));
        Assert.Equal(Utc(2026, 11, 30), profiles.Single(p => p.Id == active.Id).EffectiveTo);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Contract_activated_then_employee_created_leaves_an_active_profile_with_the_unit_and_the_regime()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(fixture);
        await using var disposeScope = scope;
        await using var disposeRoot = root;
        var employee = Guid.NewGuid();

        // The activation arrives first: nothing is known of the employee's unit or regime yet.
        await mediator.Send(new Payroll.Application.Features.InboundEvents.HandleContractActivatedCommand(Guid.NewGuid(), employee, "PERM", Utc(2026, 9, 1), 250_000m, null));
        var first = Assert.Single(await ProfilesOfAsync(db, employee));
        Assert.Equal((true, (Guid?)null, (string?)null), (first.IsActive, first.OrgUnitId, first.RegimeCode));

        // The late event is no longer ignored: it fills what is empty.
        db.ChangeTracker.Clear();
        await mediator.Send(new Payroll.Application.Features.InboundEvents.HandleEmployeeCreatedCommand(employee, UnitOfTheNewHire, "law50"));
        var filled = Assert.Single(await ProfilesOfAsync(db, employee));
        Assert.Equal((first.Id, true, (Guid?)UnitOfTheNewHire, "law50", 250_000m), (filled.Id, filled.IsActive, filled.OrgUnitId, filled.RegimeCode, filled.BaseSalary));

        // Delivered again with other values (or after a transfer set them), it overwrites nothing.
        db.ChangeTracker.Clear();
        await mediator.Send(new Payroll.Application.Features.InboundEvents.HandleEmployeeCreatedCommand(employee, Guid.NewGuid(), "contract"));
        var kept = Assert.Single(await ProfilesOfAsync(db, employee));
        Assert.Equal(((Guid?)UnitOfTheNewHire, "law50"), (kept.OrgUnitId, kept.RegimeCode));

        // And the next version carries them.
        db.ChangeTracker.Clear();
        await mediator.Send(new Payroll.Application.Features.InboundEvents.HandleContractActivatedCommand(Guid.NewGuid(), employee, "PERM", Utc(2026, 12, 1), 270_000m, null));
        var active = Assert.Single(await ProfilesOfAsync(db, employee), p => p.IsActive);
        Assert.Equal((UnitOfTheNewHire, "law50", 270_000m), (active.OrgUnitId, active.RegimeCode, active.BaseSalary));
    }
}
