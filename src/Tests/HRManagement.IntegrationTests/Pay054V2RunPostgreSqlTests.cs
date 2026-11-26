using System.Reflection;
using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Insurance;
using HRManagement.Integration.Abstractions.Payroll;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PartialDisbursement;
using Payroll.Application.Features.PayrollConfigurations;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Integration;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-54 (§2, P-7) on real runs in PostgreSQL. The same employees and the same month are calculated with the seeded version and the
/// general tables the Demo seed writes (the legacy path), then with the imported interim pack: the lines are identical, unit for unit
/// (D-PAY-1). Then the general tables are changed behind the pinned v2 version: its run, its fingerprint and its export do not move,
/// while a month the legacy version still governs follows the general tables as it always did. The insurance export takes its shares
/// from the version's rules, and a partial disbursement reads the version's priority rules.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay054V2RunPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly Guid Importer = Guid.Parse("54000000-0000-4000-8000-0000000000c1");
    private static readonly DateTime October = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OctoberEnd = new(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

    private static List<DeductionType> DemoSeedDeductionTypes()
    {
        var definitions = (Array)typeof(DemoPayrollSeeder).GetField("DeductionDefs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return definitions.Cast<(string Code, string NameAr, bool Statutory, decimal? Pct, decimal? Fixed)>()
            .Select(d => new DeductionType { Code = d.Code, NameAr = d.NameAr, IsStatutory = d.Statutory, Percentage = d.Pct, FixedAmount = d.Fixed })
            .ToList();
    }

    /// <summary>What the Demo seed gives a run: LAW_50_2004 v1 from 2000, its four holidays, the ten deduction types; and employees.</summary>
    private static async Task<PayrollConfiguration> SeedAsTheDemoSeedDoesAsync(PayrollDbContext db, int employees)
    {
        var v1 = await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
        db.OfficialHolidays.AddRange(PayrollRegimeSeed.CreateDefaultHolidays(v1.Id));
        db.DeductionTypes.AddRange(DemoSeedDeductionTypes());
        await db.SaveChangesAsync();

        await PayrollPostgresTestSupport.SeedEmployeesAsync(db, employees);
        var profiles = await db.EmployeePayProfiles.OrderBy(p => p.EmployeeId).ToListAsync();
        // PLAN-55: an account in force per employee; each run pins it at its snapshot, and the export reads the pinned account.
        foreach (var profile in profiles)
            db.Add(Pay055TestAccounts.Active(profile.EmployeeId, profile.TenantId));

        // One employee joins on Monday 5 October: his month is prorated by working days, so the holiday of 6 October counts.
        profiles[1].EffectiveFrom = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        var type = new CompensationType { Code = "ALL-NAT", NameAr = "بدل طبيعة عمل" };
        db.CompensationTypes.Add(type);
        db.AllowanceRecords.Add(new AllowanceRecord
        {
            EmployeeId = profiles[2].EmployeeId,
            AllowanceType = type,
            Amount = 12_345.67m,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return v1;
    }

    private static async Task<(Guid RunId, IReadOnlyList<PayrollDeterminismComparer.CanonicalLine> Lines)> CalculateAsync(
        PayrollDbContext db, IMediator mediator, DateTime periodStart, DateTime periodEnd)
    {
        var (created, runId, createError) = await mediator.Send(new CreatePayrollRunCommand(periodStart, periodEnd, DateTime.UtcNow));
        Assert.True(created, createError);
        var (ok, _, calculateError) = await mediator.Send(new CalculatePayrollRunCommand(runId!.Value));
        Assert.True(ok, calculateError);

        db.ChangeTracker.Clear();
        var lines = await db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == runId).ToListAsync();
        var ids = lines.Select(l => l.Id).ToList();
        var components = await db.SalaryLineComponents.AsNoTracking().Where(c => ids.Contains(c.SalaryLineId)).ToListAsync();
        return (runId.Value, PayrollDeterminismComparer.ExportCanonical(lines, components));
    }

    private static readonly Guid Approver = Guid.Parse("54000000-0000-4000-8000-0000000000c3");
    private static readonly Guid Attestor = Guid.Parse("54000000-0000-4000-8000-0000000000c4");

    /// <summary>A scope with the test's clock (23 November 2026 unless moved) and catalog: the interim gate never reads today's date.</summary>
    private async Task<(Microsoft.Extensions.DependencyInjection.ServiceProvider Root, Microsoft.Extensions.DependencyInjection.AsyncServiceScope Scope,
        PayrollDbContext Db, IMediator Mediator, Pay054Clock Clock, Pay054CatalogGuard Guard)> ScopeAsync()
    {
        var clock = new Pay054Clock(Pay054Clock.Default);
        var guard = new Pay054CatalogGuard();
        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(fixture, configureServices: Pay054TestLegal.Services(clock, guard));
        return (root, scope, db, mediator, clock, guard);
    }

    /// <summary>
    /// The interim pack as version 2 of LAW_50_2004 from 1 October 2026, taken through the product's cycle: imported by one person,
    /// approved by a second, attested by a third, published by the approver.
    /// </summary>
    private static async Task<PayrollConfiguration> ImportAndPublishAsync(PayrollDbContext db, IMediator mediator)
    {
        var imported = await mediator.Send(new ImportPayrollFrameworkPackCommand(
            Pay054FrameworkHttpTests.InterimPackJson(), null, new DateOnly(2026, 10, 1), null, Importer));
        Assert.True(imported.Success, imported.Error + string.Join(" | ", imported.Errors));
        var id = imported.FrameworkId!.Value;

        var approved = await mediator.Send(new ApprovePayrollFrameworkCommand(id, Approver));
        Assert.True(approved.Success, approved.Error);
        var attested = await mediator.Send(new AttestPayrollFrameworkCommand(id, Attestor));
        Assert.True(attested.Success, attested.Error);
        var published = await mediator.Send(new PublishPayrollFrameworkCommand(id, Approver, null));
        Assert.True(published.Success, published.Error);

        db.ChangeTracker.Clear();
        return await db.PayrollConfigurations.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private static async Task<SocialInsuranceReportRequest> ExportInsuranceAsync(PayrollDbContext db, Guid runId)
    {
        // The export takes a posted run; the test posts it directly for the export and puts its status back (the run's own cycle
        // is not what is measured here, and the next step cancels the run to calculate the month again).
        var status = await db.PayrollRuns.AsNoTracking().Where(r => r.Id == runId).Select(r => r.Status).SingleAsync();
        await db.PayrollRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, PayrollRunStatus.Posted));
        db.ChangeTracker.Clear();

        SocialInsuranceReportRequest? captured = null;
        var reporter = new Mock<ISocialInsuranceReporter>();
        reporter.Setup(r => r.SubmitAsync(It.IsAny<SocialInsuranceReportRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SocialInsuranceReportRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new IntegrationResult(true));
        var lookup = new Mock<IEmployeeLookup>();
        lookup.Setup(l => l.GetEmployeeDisplayNamesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        await new PayrollExternalExportService(db, lookup.Object, reporter.Object).ExportInsuranceAsync(runId);
        db.ChangeTracker.Clear();
        await db.PayrollRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status));
        return captured!;
    }

    private static List<(Guid Employee, decimal Base, decimal EmployeeShare, decimal EmployerShare)> Shares(SocialInsuranceReportRequest request) =>
        request.Lines.OrderBy(l => l.EmployeeId).Select(l => (l.EmployeeId, l.InsurableWageBase, l.EmployeeContribution, l.EmployerContribution)).ToList();

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_same_employees_and_month_give_identical_lines_with_the_seeded_version_and_with_the_imported_pack()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (root, scope, db, mediator, _, _) = await ScopeAsync();
        await using var disposeScope = scope;
        await using var disposeRoot = root;
        var v1 = await SeedAsTheDemoSeedDoesAsync(db, employees: 8);

        // A: the seeded version (v1), the general deduction types and holidays. The legacy path, named in the job's log.
        var (runA, linesA) = await CalculateAsync(db, mediator, October, OctoberEnd);
        var pinnedA = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runA);
        Assert.Equal(v1.Id, pinnedA.PayrollConfigurationId);
        Assert.Equal(v1.ContentHash, pinnedA.ConfigContentHash);
        var jobA = await db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.PayrollRunId == runA);
        Assert.Contains(PayrollFrameworkReferenceData.LegacyLogLine, jobA.Log ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(8, linesA.Count);
        // Every line carries the four statutory deductions of the general table, and the prorated employee differs from a full month.
        Assert.All(linesA, l => Assert.Equal(4, l.Components.Count(c => c.ComponentType == (int)SalaryComponentType.Deduction)));
        var insuranceA = Shares(await ExportInsuranceAsync(db, runA));
        await PayrollPostgresTestSupport.CancelRunForDualTestAsync(db, runA);

        // B: the interim pack, imported and published from the same month. The run is pinned to it and reads only it.
        var v2 = await ImportAndPublishAsync(db, mediator);
        var (runB, linesB) = await CalculateAsync(db, mediator, October, OctoberEnd);
        var pinnedB = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runB);
        Assert.Equal(v2.Id, pinnedB.PayrollConfigurationId);
        Assert.Equal(v2.ContentHash, pinnedB.ConfigContentHash);
        Assert.NotEqual(v1.ContentHash, v2.ContentHash);
        var jobB = await db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.PayrollRunId == runB);
        Assert.DoesNotContain(PayrollFrameworkReferenceData.LegacyLogLine, jobB.Log ?? string.Empty, StringComparison.Ordinal);

        // D-PAY-1: identical lines, unit for unit.
        Assert.Null(PayrollDeterminismComparer.BuildDiff(linesA, linesB));
        Assert.True(PayrollDeterminismComparer.AreBitForBitEqual(linesA, linesB));

        // The insurance export of the v2 run takes its shares from the version's rules, and they are what the v1 run exported.
        var insuranceB = Shares(await ExportInsuranceAsync(db, runB));
        Assert.Equal(insuranceA, insuranceB);
        Assert.All(insuranceB, l =>
        {
            Assert.Equal(decimal.Round(l.Base * 0.09m, 2, MidpointRounding.AwayFromZero), l.EmployeeShare);    // DED-SOC 7% + DED-HEALTH 2%
            Assert.Equal(decimal.Round(l.Base * 0.14m, 2, MidpointRounding.AwayFromZero), l.EmployerShare);    // DED-SOC's employer share
        });
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Editing_the_general_tables_after_pinning_moves_neither_a_v2_result_nor_its_fingerprint_nor_its_export()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (root, scope, db, mediator, _, _) = await ScopeAsync();
        await using var disposeScope = scope;
        await using var disposeRoot = root;
        var v1 = await SeedAsTheDemoSeedDoesAsync(db, employees: 6);
        var v2 = await ImportAndPublishAsync(db, mediator);

        var (runBefore, before) = await CalculateAsync(db, mediator, October, OctoberEnd);
        var exportBefore = Shares(await ExportInsuranceAsync(db, runBefore));
        await PayrollPostgresTestSupport.CancelRunForDualTestAsync(db, runBefore);

        // Someone edits the general tables: the statutory percentages, a new statutory type, a general holiday on a working day, and
        // renames the insurance type (the legacy export finds insurance by name).
        foreach (var type in await db.DeductionTypes.Where(t => t.IsStatutory).ToListAsync())
        {
            type.Percentage = 40m;
            type.NameAr = "معدّل " + type.Code;
        }

        db.DeductionTypes.Add(new DeductionType { Code = "DED-AFTER", NameAr = "اقتطاع أضيف بعد التثبيت", IsStatutory = true, FixedAmount = 7_777m });
        db.OfficialHolidays.AddRange(
            new OfficialHoliday { Date = new DateOnly(2026, 10, 7), NameAr = "عطلة عامة أضيفت", IsRecurring = false },
            new OfficialHoliday { Date = new DateOnly(2026, 9, 7), NameAr = "عطلة عامة أضيفت", IsRecurring = false });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // The same month again, on a new snapshot of the same version: nothing moved.
        var (runAfter, after) = await CalculateAsync(db, mediator, October, OctoberEnd);
        Assert.Null(PayrollDeterminismComparer.BuildDiff(before, after));
        Assert.Equal(exportBefore, Shares(await ExportInsuranceAsync(db, runAfter)));
        var stored = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets).Include(c => c.StatutoryDeductions).Include(c => c.Holidays)
            .Include(c => c.PriorityRules).Include(c => c.FieldSources)
            .AsNoTracking().SingleAsync(c => c.Id == v2.Id);
        Assert.Equal(v2.ContentHash, stored.ContentHash);
        Assert.Equal(v2.ContentHash, PayrollConfigurationHashService.ComputeContentHash(stored));          // the same after a reload
        Assert.Equal(v2.ContentHash, (await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runAfter)).ConfigContentHash);
        Assert.All(after, l => Assert.DoesNotContain(l.Components, c => c.ComponentName.StartsWith("معدّل", StringComparison.Ordinal) || c.ComponentName == "اقتطاع أضيف بعد التثبيت"));

        // September is still the superseded version's month (P-6), and that version is older than PLAN-54: its run reads the general
        // tables as it always did, so it sees the edit. The legacy path is unchanged, and it is the only one the edit reaches.
        var (runSeptember, september) = await CalculateAsync(
            db, mediator, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc));
        var pinnedSeptember = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runSeptember);
        Assert.Equal(v1.Id, pinnedSeptember.PayrollConfigurationId);
        Assert.Equal(PayrollConfigPublicationStatus.Superseded, (await db.PayrollConfigurations.AsNoTracking().SingleAsync(c => c.Id == v1.Id)).PublicationStatus);
        Assert.All(september, l => Assert.Contains(l.Components, c => c.ComponentName == "اقتطاع أضيف بعد التثبيت" && c.Amount == -7_777m));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_calculation_with_an_interim_framework_stops_when_its_exception_has_expired_and_a_legacy_month_goes_on()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (root, scope, db, mediator, clock, guard) = await ScopeAsync();
        await using var disposeScope = scope;
        await using var disposeRoot = root;
        var v1 = await SeedAsTheDemoSeedDoesAsync(db, employees: 4);
        var v2 = await ImportAndPublishAsync(db, mediator);

        // While the exceptions are active the month calculates, and the job's log says the framework is interim and under what.
        var (october, lines) = await CalculateAsync(db, mediator, October, OctoberEnd);
        Assert.Equal(4, lines.Count);
        var job = await db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.PayrollRunId == october);
        Assert.Contains("إطار مؤقت — استثناء EX-STR2-LAW50-UNCERTIFIED، EX-STR2-TAX-INTERIM", job.Log ?? string.Empty, StringComparison.Ordinal);

        // 1 February 2027 in Damascus (31 January, 21:30 UTC): both exceptions ended yesterday. A new month is refused, closed.
        clock.Now = new DateTimeOffset(2027, 1, 31, 21, 30, 0, TimeSpan.Zero);
        var (created, november, createError) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow));
        Assert.True(created, createError);
        var (calculated, generated, error) = await mediator.Send(new CalculatePayrollRunCommand(november!.Value));
        Assert.False(calculated);
        Assert.Equal(0, generated);
        Assert.Equal("انتهى الاستثناء القانوني EX-STR2-LAW50-UNCERTIFIED؛ انتهى الاستثناء القانوني EX-STR2-TAX-INTERIM", error);

        db.ChangeTracker.Clear();
        var refusedRun = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == november);
        Assert.Equal(PayrollRunStatus.Draft, refusedRun.Status);                      // not started
        Assert.Null(refusedRun.PayrollConfigurationId);                               // not pinned
        Assert.Null(refusedRun.ConfigContentHash);
        Assert.Equal(0, await db.SalaryLines.CountAsync(l => l.PayrollRunId == november));
        var refusedJob = await db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.PayrollRunId == november);
        Assert.Equal(PayrollRunJobStatus.Failed, refusedJob.Status);                  // the job's log carries the reason
        Assert.Equal(error, refusedJob.Error);
        Assert.Contains("انتهى الاستثناء القانوني EX-STR2-TAX-INTERIM", refusedJob.Log ?? string.Empty, StringComparison.Ordinal);

        // That row is not a calculation that can be resumed: resuming it would mark an empty, unpinned run calculated and frozen.
        var resumeRefusal = await mediator.Send(new ResumePayrollRunJobCommand(refusedJob.Id));
        Assert.False(resumeRefusal.Success);
        Assert.Equal(ResumePayrollRunJobHandler.NeverStarted, resumeRefusal.Error);
        db.ChangeTracker.Clear();
        refusedRun = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == november);
        Assert.Equal(PayrollRunStatus.Draft, refusedRun.Status);
        Assert.False(refusedRun.ResultsFrozen);
        Assert.Null(refusedRun.PayrollConfigurationId);
        Assert.Equal(0, await db.SalaryLines.CountAsync(l => l.PayrollRunId == november));
        Assert.Equal(PayrollRunJobStatus.Failed, (await db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.Id == refusedJob.Id)).Status);
        // Asking again is refused again, on the same row: the refusals do not pile up.
        Assert.False((await mediator.Send(new CalculatePayrollRunCommand(november.Value))).Success);
        db.ChangeTracker.Clear();
        var sameRow = await db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.PayrollRunId == november);
        Assert.Equal(refusedJob.Id, sameRow.Id);
        Assert.Equal(2, sameRow.Attempt);

        // A correction is a calculation with the source's pinned version: October's is refused too, and no correction run is written.
        await db.PayrollRuns.Where(r => r.Id == october).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, PayrollRunStatus.Posted));
        var correction = await mediator.Send(new CreateCorrectionRunCommand(october, new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(correction.Success);
        Assert.Equal(error, correction.Error);
        Assert.Equal(0, await db.PayrollRuns.CountAsync(r => r.RunType == PayrollRunType.Correction));
        await db.PayrollRuns.Where(r => r.Id == october).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, PayrollRunStatus.Calculated));
        db.ChangeTracker.Clear();

        // A paused calculation of the interim version is not resumed either, and its job says why; it stays paused for the day the
        // exception is renewed.
        var pinned = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Calculating,
            PayrollConfigurationId = v2.Id,
            ConfigContentHash = v2.ContentHash,
        };
        db.PayrollRuns.Add(pinned);
        var paused = new PayrollRunJob { PayrollRunId = pinned.Id, Phase = PayrollRunJobPhase.Calculate, Status = PayrollRunJobStatus.Paused, Attempt = 1 };
        // A job that started has its partitions (a job without one never took a snapshot and is not resumed at all).
        paused.Partitions.Add(new PayrollRunPartition { PartitionIndex = 0, Status = PayrollRunPartitionStatus.Pending, EmployeeCount = 1 });
        db.PayrollRunJobs.Add(paused);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var resumed = await mediator.Send(new StartPayrollRunJobCommand(pinned.Id));
        Assert.False(resumed.Success);
        Assert.Equal(error, resumed.Error);
        db.ChangeTracker.Clear();
        var stillPaused = await db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.Id == paused.Id);
        Assert.Equal(PayrollRunJobStatus.Paused, stillPaused.Status);
        Assert.Equal(1, stillPaused.Attempt);
        Assert.Equal(error, stillPaused.Error);
        // Nor by the resume command itself (the «استئناف» button and the API's resume): the same refusal, the job as it was.
        var resumedByCommand = await mediator.Send(new ResumePayrollRunJobCommand(paused.Id));
        Assert.False(resumedByCommand.Success);
        Assert.Equal(error, resumedByCommand.Error);
        db.ChangeTracker.Clear();
        stillPaused = await db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.Id == paused.Id);
        Assert.Equal(PayrollRunJobStatus.Paused, stillPaused.Status);
        Assert.Equal(1, stillPaused.Attempt);
        Assert.Equal(0, await db.SalaryLines.CountAsync(l => l.PayrollRunId == pinned.Id));

        // September is the superseded legacy version's month: it cites no exception and calculates as before.
        var (september, septemberLines) = await CalculateAsync(
            db, mediator, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(4, septemberLines.Count);
        Assert.Equal(v1.Id, (await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == september)).PayrollConfigurationId);

        // The exception is renewed in the catalog: the refused month calculates.
        guard.LastDays[Pay054CatalogGuard.Tax] = new DateOnly(2027, 12, 31);
        guard.LastDays[Pay054CatalogGuard.Law50] = new DateOnly(2027, 12, 31);
        var (again, againLines, againError) = await mediator.Send(new CalculatePayrollRunCommand(november.Value));
        Assert.True(again, againError);
        Assert.Equal(4, againLines);
    }

    /// <summary>
    /// PLAN-54 (§3): an approval fixes the content it fingerprinted, also when two people act at the same moment. Another person's
    /// request is landed between a handler's read and its save (at the handler's first read of the clock): the version's row version
    /// has moved, so the late save writes nothing and answers a conflict.
    /// </summary>
    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_request_that_lands_between_another_persons_read_and_save_is_a_conflict_and_nothing_of_the_late_one_is_written()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (root, scope, db, mediator, clock, _) = await ScopeAsync();
        await using var disposeScope = scope;
        await using var disposeRoot = root;

        // Another person's request, in its own scope (its own context and transaction), run to its end from inside the first one.
        T Meanwhile<T>(MediatR.IRequest<T> request) => Task.Run(async () =>
        {
            await using var other = root.CreateAsyncScope();
            return await other.ServiceProvider.GetRequiredService<IMediator>().Send(request);
        }).GetAwaiter().GetResult();

        var imported = await mediator.Send(new ImportPayrollFrameworkPackCommand(
            Pay054FrameworkHttpTests.InterimPackJson(), "LAW_50_2004_RACE", new DateOnly(2031, 1, 1), null, Importer));
        Assert.True(imported.Success, imported.Error);
        var id = imported.FrameworkId!.Value;

        // 1. The editor moves the draft's date while the approver's request holds the content it read: the approval is a conflict.
        PayrollFrameworkResult? edit = null;
        clock.BeforeNextRead = () => edit = Meanwhile(new SetPayrollFrameworkEffectiveFromCommand(id, new DateOnly(2031, 3, 1), Importer));
        var approval = await mediator.Send(new ApprovePayrollFrameworkCommand(id, Approver));
        Assert.True(edit is { Success: true }, edit?.Error);
        Assert.False(approval.Success);
        Assert.Equal(409, approval.StatusCode);
        Assert.Equal(PayrollFrameworkImportMessages.ChangedConcurrently, approval.Error);
        db.ChangeTracker.Clear();
        var draft = await db.PayrollConfigurations.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(PayrollConfigPublicationStatus.Draft, draft.PublicationStatus);  // not approved with a fingerprint of other content
        Assert.Null(draft.ContentHash);
        Assert.Null(draft.ApprovedBy);
        Assert.Equal(new DateTime(2031, 3, 1, 0, 0, 0, DateTimeKind.Utc), draft.EffectiveFrom);

        // 2. The other way round: the approval lands while the editor's request holds the draft. The edit is a conflict, and the
        // approved content is the content that was fingerprinted.
        PayrollFrameworkResult? landed = null;
        clock.BeforeNextRead = () => landed = Meanwhile(new ApprovePayrollFrameworkCommand(id, Approver));
        var lateEdit = await mediator.Send(new SetPayrollFrameworkEffectiveFromCommand(id, new DateOnly(2031, 6, 1), Importer));
        Assert.True(landed is { Success: true }, landed?.Error);
        Assert.False(lateEdit.Success);
        Assert.Equal(409, lateEdit.StatusCode);
        db.ChangeTracker.Clear();
        var approved = await mediator.Send(new ExportPayrollFrameworkQuery(id));
        var version = await db.PayrollConfigurations.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(PayrollConfigPublicationStatus.Approved, version.PublicationStatus);
        Assert.Equal(new DateTime(2031, 3, 1, 0, 0, 0, DateTimeKind.Utc), version.EffectiveFrom);
        Assert.NotNull(approved);
        var reloaded = await scope.ServiceProvider.GetRequiredService<IPayrollRepository>().GetConfigurationByIdAsync(id, CancellationToken.None);
        Assert.True(PayrollFrameworkGovernance.FingerprintHolds(reloaded!));

        // 3. Two publications that supersede the same version: the first one to save wins, the other is a conflict and stays approved.
        Assert.True((await mediator.Send(new AttestPayrollFrameworkCommand(id, Attestor))).Success);
        Assert.True((await mediator.Send(new PublishPayrollFrameworkCommand(id, Approver, null))).Success);
        async Task<Guid> ApprovedAndAttestedAsync(DateOnly from)
        {
            var next = await mediator.Send(new ImportPayrollFrameworkPackCommand(Pay054FrameworkHttpTests.InterimPackJson(), "LAW_50_2004_RACE", from, null, Importer));
            Assert.True(next.Success, next.Error);
            Assert.True((await mediator.Send(new ApprovePayrollFrameworkCommand(next.FrameworkId!.Value, Approver))).Success);
            Assert.True((await mediator.Send(new AttestPayrollFrameworkCommand(next.FrameworkId.Value, Attestor))).Success);
            return next.FrameworkId.Value;
        }

        var second = await ApprovedAndAttestedAsync(new DateOnly(2031, 7, 1));
        var third = await ApprovedAndAttestedAsync(new DateOnly(2031, 9, 1));
        PayrollFrameworkResult? other = null;
        clock.BeforeNextRead = () => other = Meanwhile(new PublishPayrollFrameworkCommand(third, Attestor, null));
        var late = await mediator.Send(new PublishPayrollFrameworkCommand(second, Approver, null));
        Assert.True(other is { Success: true }, other?.Error);
        Assert.False(late.Success);
        Assert.Equal(409, late.StatusCode);
        db.ChangeTracker.Clear();
        var versions = await db.PayrollConfigurations.AsNoTracking().Where(c => c.Code == "LAW_50_2004_RACE").ToDictionaryAsync(c => c.Id);
        Assert.Equal(PayrollConfigPublicationStatus.Superseded, versions[id].PublicationStatus);
        Assert.Equal(new DateTime(2031, 8, 31, 0, 0, 0, DateTimeKind.Utc), versions[id].EffectiveTo);    // ended once, by the third
        Assert.Equal(PayrollConfigPublicationStatus.Approved, versions[second].PublicationStatus);
        Assert.Null(versions[second].PublishedBy);
        Assert.Equal(PayrollConfigPublicationStatus.Published, versions[third].PublicationStatus);
        Assert.Equal(1, await db.PayrollConfigChangeNotices.CountAsync(n => n.PayrollConfigurationId == third));
        Assert.Equal(0, await db.PayrollConfigChangeNotices.CountAsync(n => n.PayrollConfigurationId == second));
    }

    /// <summary>
    /// PLAN-54 (§4, D-PAY-6): on a real run, a manual input counts only after a second person approved it. The run is exercised with
    /// an allowance and a deduction. An adjustment is proved at the loader (<c>Pay054ManualInputApprovalTests</c>): a run that holds
    /// any non-zero adjustment, manual or written by a consumer, is refused today by the run's own reconciliation («عدم تطابق صافي
    /// السطر»: it expects net = gross − deductions and the engine adds the adjustment). That defect is older than this plan and is
    /// PLAN-56's (the run's state machine and reconciliation); it is recorded in the report, not changed here.
    /// </summary>
    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_manual_input_enters_no_run_before_a_second_person_approves_it_and_enters_the_next_run_after()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (root, scope, db, mediator, _, _) = await ScopeAsync();
        await using var disposeScope = scope;
        await using var disposeRoot = root;
        await SeedAsTheDemoSeedDoesAsync(db, employees: 3);
        var employee = await db.EmployeePayProfiles.AsNoTracking().OrderBy(p => p.EmployeeId).Select(p => p.EmployeeId).FirstAsync();
        var type = await db.CompensationTypes.AsNoTracking().Select(t => t.Id).SingleAsync();
        var maker = Importer;
        var checker = Approver;

        async Task<Guid> EnterAsync(Payroll.Application.Features.ManualInputs.ManualInputKind kind, decimal amount)
        {
            var created = await mediator.Send(new Payroll.Application.Features.ManualInputs.CreateManualInputCommand(
                kind, employee, amount, "قرار الوزير 12 لعام 2026", maker,
                AllowanceTypeId: kind == Payroll.Application.Features.ManualInputs.ManualInputKind.Allowance ? type : null,
                DeductionType: kind == Payroll.Application.Features.ManualInputs.ManualInputKind.Deduction ? "Other" : null,
                EffectiveFrom: kind == Payroll.Application.Features.ManualInputs.ManualInputKind.Allowance ? new DateOnly(2026, 10, 1) : null));
            Assert.True(created.Success, created.Error);
            Assert.Equal(ManualInputApprovalStatus.PendingApproval, created.Status);
            return created.InputId!.Value;
        }

        var allowance = await EnterAsync(Payroll.Application.Features.ManualInputs.ManualInputKind.Allowance, 1_111.11m);
        var deduction = await EnterAsync(Payroll.Application.Features.ManualInputs.ManualInputKind.Deduction, 222.22m);
        // Entered and left pending to the end: a pending adjustment must not reach a run (and so must not trip its reconciliation).
        var adjustment = await EnterAsync(Payroll.Application.Features.ManualInputs.ManualInputKind.Adjustment, 333.33m);
        var refused = await EnterAsync(Payroll.Application.Features.ManualInputs.ManualInputKind.Allowance, 999.99m);

        // Pending: the month's run does not see any of them.
        var (first, before) = await CalculateAsync(db, mediator, October, OctoberEnd);
        var mine = before.Single(l => l.EmployeeId == employee);
        decimal[] manual = [1_111.11m, -222.22m, 333.33m, 999.99m];
        Assert.DoesNotContain(mine.Components, c => manual.Contains(c.Amount));
        Assert.Null((await db.DeductionRecords.AsNoTracking().SingleAsync(d => d.Id == deduction)).AppliedInRunId);
        Assert.Null((await db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == adjustment)).PayrollRunId);
        await PayrollPostgresTestSupport.CancelRunForDualTestAsync(db, first);

        // The maker cannot approve his own; a second person approves two and rejects one.
        var own = await mediator.Send(new Payroll.Application.Features.ManualInputs.DecideManualInputCommand(
            Payroll.Application.Features.ManualInputs.ManualInputKind.Allowance, allowance, Payroll.Application.Features.ManualInputs.ManualInputDecision.Approved, null, maker));
        Assert.Equal(403, own.StatusCode);
        foreach (var (kind, id) in new[]
                 {
                     (Payroll.Application.Features.ManualInputs.ManualInputKind.Allowance, allowance),
                     (Payroll.Application.Features.ManualInputs.ManualInputKind.Deduction, deduction),
                 })
        {
            var approved = await mediator.Send(new Payroll.Application.Features.ManualInputs.DecideManualInputCommand(
                kind, id, Payroll.Application.Features.ManualInputs.ManualInputDecision.Approved, null, checker));
            Assert.True(approved.Success, approved.Error);
        }

        Assert.True((await mediator.Send(new Payroll.Application.Features.ManualInputs.DecideManualInputCommand(
            Payroll.Application.Features.ManualInputs.ManualInputKind.Allowance, refused,
            Payroll.Application.Features.ManualInputs.ManualInputDecision.Rejected, "لا سند قانوني", checker))).Success);

        // The next run takes the two approved inputs, and never the rejected one nor the adjustment still pending. Everyone else's
        // lines are as before.
        var (second, after) = await CalculateAsync(db, mediator, October, OctoberEnd);
        var mineAfter = after.Single(l => l.EmployeeId == employee);
        Assert.Contains(mineAfter.Components, c => c.ComponentType == (int)SalaryComponentType.Allowance && c.Amount == 1_111.11m);
        Assert.Contains(mineAfter.Components, c => c.ComponentType == (int)SalaryComponentType.Deduction && c.Amount == -222.22m);
        Assert.DoesNotContain(mineAfter.Components, c => c.ComponentType == (int)SalaryComponentType.Adjustment);
        Assert.DoesNotContain(mineAfter.Components, c => c.Amount == 999.99m);
        Assert.Null((await db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.Id == adjustment)).PayrollRunId);
        Assert.Equal(mine.GrossPay + 1_111.11m, mineAfter.GrossPay);
        Assert.Equal(
            before.Where(l => l.EmployeeId != employee).Select(l => (l.EmployeeId, l.GrossPay, l.TotalDeductions, l.NetPay)),
            after.Where(l => l.EmployeeId != employee).Select(l => (l.EmployeeId, l.GrossPay, l.TotalDeductions, l.NetPay)));
        Assert.Equal(second, (await db.DeductionRecords.AsNoTracking().SingleAsync(d => d.Id == deduction)).AppliedInRunId);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_partial_disbursement_of_a_v2_run_reads_its_versions_rules_and_a_legacy_run_the_general_ones()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (root, scope, db, mediator, _, _) = await ScopeAsync();
        await using var disposeScope = scope;
        await using var disposeRoot = root;
        var v1 = await SeedAsTheDemoSeedDoesAsync(db, employees: 3);
        var v2 = await ImportAndPublishAsync(db, mediator);

        // PLAN-58 (D-PAY-2): a funding decision is taken on the posting's batch. Each case posts its own run (a batch is funded once).
        async Task<(PayrollRun Run, Guid BatchId, Guid? RuleSet)> PostedRunAsync(Guid? configurationId, Guid? ruleSet, int month)
        {
            var run = new PayrollRun
            {
                PeriodStart = new DateTime(2026, month, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, month, 28, 0, 0, 0, DateTimeKind.Utc),
                RunDate = DateTime.UtcNow,
                Status = PayrollRunStatus.Posted,
                RunType = PayrollRunType.Regular,
                TotalNetPay = 162_000m,
                ValidatedAtCalculate = true,
                ResultsFrozen = true,
                PayrollConfigurationId = configurationId,
            };
            run.SalaryLines.Add(new SalaryLine { EmployeeId = Guid.NewGuid(), GrossPay = 100_000m, TotalDeductions = 10_000m, NetPay = 90_000m });
            run.SalaryLines.Add(new SalaryLine { EmployeeId = Guid.NewGuid(), GrossPay = 80_000m, TotalDeductions = 8_000m, NetPay = 72_000m });
            db.PayrollRuns.Add(run);
            var batch = await scope.ServiceProvider.GetRequiredService<Payroll.Application.Services.PaymentBatchIssuer>()
                .IssueAsync(run, run.SalaryLines.ToList(), PaymentBatchSource.PayrollRunPosted, CancellationToken.None);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return (run, batch.Id, ruleSet);
        }

        async Task<(bool Success, string? Error, decimal TotalDisbursed, bool InvariantOk)> PreviewAsync((PayrollRun Run, Guid BatchId, Guid? RuleSet) x)
        {
            var result = await mediator.Send(new CreateFundingDecisionCommand(x.BatchId, 97_200m, 0.60m, x.RuleSet, Guid.NewGuid()));
            db.ChangeTracker.Clear();
            var funded = await db.PaymentBatches.AsNoTracking().SingleAsync(b => b.Id == x.BatchId);
            var orders = await db.PaymentOrders.AsNoTracking().Where(o => o.PaymentBatchId == x.BatchId && o.Status != PaymentOrderStatus.Cancelled).SumAsync(o => o.Amount);
            return (result.Success, result.Error, result.Success ? orders : 0m, orders + funded.ArrearsAmount == 162_000m);
        }

        // No general rule exists: a legacy run finds none, although the v2 version holds three (they never leak into a legacy run).
        // And none of the three is an active row of the general table: the release before PLAN-54 reads every active row for a
        // decision that names no rule set, so a rollback to it finds none either.
        Assert.Equal(3, await db.DisbursementPriorityRules.CountAsync(r => r.PayrollConfigurationId == v2.Id && !r.IsActive));
        Assert.Equal(0, await db.DisbursementPriorityRules.CountAsync(r => r.IsActive));
        var legacy = await PostedRunAsync(v1.Id, ruleSet: null, month: 1);
        var unpinned = await PostedRunAsync(null, ruleSet: null, month: 2);
        Assert.Equal("لا توجد قواعد أولوية للصرف الجزئي.", (await PreviewAsync(legacy)).Error);
        Assert.Equal("لا توجد قواعد أولوية للصرف الجزئي.", (await PreviewAsync(unpinned)).Error);

        // A v2 run is disbursed by its version's rules, with no general rule at all, whether the decision names no rule set or the version.
        var current = await PostedRunAsync(v2.Id, ruleSet: null, month: 10);
        var named = await PostedRunAsync(v2.Id, ruleSet: v2.Id, month: 11);
        var viaVersion = await PreviewAsync(current);
        Assert.True(viaVersion.Success, viaVersion.Error);
        Assert.True(viaVersion.InvariantOk);
        Assert.Equal(97_200m, viaVersion.TotalDisbursed);
        Assert.True((await PreviewAsync(named)).Success);

        // A general rule set appears. The legacy runs read it (as before: by the decision's rule set, or the general active rules);
        // the v2 run does not, and a decision that names it for a v2 run is refused.
        var generalSet = Guid.NewGuid();
        db.DisbursementPriorityRules.Add(new DisbursementPriorityRule { RuleSetId = generalSet, Category = "RegularPayroll", Priority = 1, LegalSource = "تعميم الخزينة", IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.True((await PreviewAsync(legacy)).Success);
        Assert.True((await PreviewAsync(unpinned)).Success);
        // (a refused decision changed nothing: the same batches take the decision once the rule exists)
        Assert.True((await PreviewAsync(await PostedRunAsync(v1.Id, ruleSet: generalSet, month: 3))).Success);
        var foreign = await PreviewAsync(await PostedRunAsync(v2.Id, ruleSet: generalSet, month: 12));
        Assert.False(foreign.Success);
        Assert.Equal("قرار التمويل يسمّي مجموعة قواعد غير قواعد الإطار القانوني المثبت على التشغيل.", foreign.Error);

        // A v2 version with no rule of its own is not disbursed by the general ones (a fresh run: the earlier one is funded already).
        await db.DisbursementPriorityRules.Where(r => r.PayrollConfigurationId == v2.Id).ExecuteDeleteAsync();
        Assert.Equal("لا توجد قواعد أولوية للصرف الجزئي.", (await PreviewAsync(await PostedRunAsync(v2.Id, ruleSet: null, month: 9))).Error);
    }
}
