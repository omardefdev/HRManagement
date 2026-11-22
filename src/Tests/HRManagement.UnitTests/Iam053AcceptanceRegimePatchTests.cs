using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults.Seeding;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-53 (P-10): <see cref="AcceptancePayrollRegimePatch"/> gives the acceptance seed's current employees a service regime so payroll
/// runs pass the service-regime gate. It writes exactly what the two regime services write (proved here by running both on equal
/// data), once per record; it leaves ended records, classified records, other exception types and other tenants alone; it deletes
/// nothing; and a second run does nothing.
/// </summary>
public sealed class Iam053AcceptanceRegimePatchTests
{
    private static readonly Guid AgencyA = AcceptanceSeedCatalog.AgencyAId;
    private static readonly Guid AgencyB = AcceptanceSeedCatalog.AgencyBId;
    private static readonly Guid NotAnAgency = Guid.Parse("53000000-0000-4000-8000-0000000000ff");
    private static readonly DateTime Hired = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Services(
        HRCoreDbContext Db, ServiceRegimeExceptionService Exceptions, ServiceRegimeTransitionService Transitions, ServiceRegimePayrollBlockService Blocks);

    private static async Task<Services> StartAsync()
    {
        var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await LegislationRegisterSeeder.SeedAsync(db);
        var legislation = new LegislationRegisterProvider(db);
        await ServiceRegimeSeeder.SeedAsync(db, legislation);
        var validator = new ServiceRegimeValidator(db, new RecruitmentLegalSourceValidator(legislation));
        var transitions = new ServiceRegimeTransitionService(db, validator);
        return new Services(db, new ServiceRegimeExceptionService(db, transitions), transitions, new ServiceRegimePayrollBlockService(db));
    }

    private static async Task<(Employee Employee, EmploymentRecord Record)> AddEmployeeAsync(
        HRCoreDbContext db, Guid tenant, string nationalId, string? regime = null, DateTime? endDate = null)
    {
        var employee = new Employee
        {
            TenantId = tenant,
            NationalId = nationalId,
            FirstName = "موظف",
            LastName = "القبول",
            FullName = "موظف القبول",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male,
            HireDate = Hired,
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.Active,
        };
        var record = new EmploymentRecord
        {
            TenantId = tenant,
            Employee = employee,
            PositionTitle = "محاسب",
            StartDate = Hired,
            EndDate = endDate,
            LegalRegimeId = regime,
        };
        db.AddRange(employee, record);
        await db.SaveChangesAsync();
        return (employee, record);
    }

    private static async Task<ServiceRegimeException> OpenAsync(
        Services s, Guid tenant, Employee employee, EmploymentRecord? record, ServiceRegimeExceptionType type = ServiceRegimeExceptionType.UnclassifiedRegime)
    {
        if (type == ServiceRegimeExceptionType.UnclassifiedRegime && record?.LegalRegimeId is null)
        {
            var enqueued = await s.Exceptions.EnqueueIfUnclassifiedAsync(employee, record, ServiceRegimeExceptionSource.Backfill, null);
            enqueued!.TenantId = tenant;
            await s.Db.SaveChangesAsync();
            return enqueued;
        }

        var entry = new ServiceRegimeException
        {
            TenantId = tenant,
            EmployeeId = employee.Id,
            EmploymentRecordId = record?.Id,
            ExceptionType = type,
            Status = ServiceRegimeExceptionStatus.Open,
            SourceContext = ServiceRegimeExceptionSource.Backfill,
            Reason = "حالة تحتاج مراجعة",
        };
        s.Db.ServiceRegimeExceptions.Add(entry);
        await s.Db.SaveChangesAsync();
        return entry;
    }

    private static async Task<bool> BlockedAsync(Services s, Guid employeeId) =>
        (await s.Blocks.GetBlockersAsync([employeeId])).Single().Blocked;

    [Fact]
    public async Task The_patch_writes_exactly_what_the_two_regime_services_write()
    {
        // The same data twice: once resolved through the services, once by the patch.
        var viaServices = await StartAsync();
        var (employeeS, recordS) = await AddEmployeeAsync(viaServices.Db, AgencyA, "5300000001");
        var exceptionS = await OpenAsync(viaServices, AgencyA, employeeS, recordS);
        Assert.Null(await viaServices.Exceptions.ResolveAsync(
            exceptionS.Id, AcceptancePayrollRegimePatch.RegimeId, AcceptancePayrollRegimePatch.DecisionRef, recordS.StartDate, null,
            AcceptancePayrollRegimePatch.ResolutionAr, resolvedBy: null));

        var viaPatch = await StartAsync();
        var (employeeP, recordP) = await AddEmployeeAsync(viaPatch.Db, AgencyA, "5300000001");
        var exceptionP = await OpenAsync(viaPatch, AgencyA, employeeP, recordP);
        var result = await AcceptancePayrollRegimePatch.ApplyAsync(viaPatch.Db);
        Assert.Equal(new AcceptancePayrollRegimePatch.Result(1, 1), result);

        // The employment record.
        Assert.Equal("law50", (await viaServices.Db.EmploymentRecords.AsNoTracking().SingleAsync(r => r.Id == recordS.Id)).LegalRegimeId);
        Assert.Equal("law50", (await viaPatch.Db.EmploymentRecords.AsNoTracking().SingleAsync(r => r.Id == recordP.Id)).LegalRegimeId);

        // The append-only transition row: one, with the same fields.
        var transitionS = await viaServices.Db.EmploymentRegimeTransitions.AsNoTracking().SingleAsync();
        var transitionP = await viaPatch.Db.EmploymentRegimeTransitions.AsNoTracking().SingleAsync();
        Assert.Equal(
            (transitionS.FromRegimeId, transitionS.ToRegimeId, transitionS.EffectiveFrom, transitionS.TransitionDecisionRef, transitionS.ConditionCode,
                transitionS.ConflictNotes, transitionS.AppliedBy),
            (transitionP.FromRegimeId, transitionP.ToRegimeId, transitionP.EffectiveFrom, transitionP.TransitionDecisionRef, transitionP.ConditionCode,
                transitionP.ConflictNotes, transitionP.AppliedBy));
        Assert.Equal((employeeP.Id, recordP.Id, AgencyA), (transitionP.EmployeeId, transitionP.EmploymentRecordId, transitionP.TenantId));
        Assert.Equal((employeeS.Id, recordS.Id), (transitionS.EmployeeId, transitionS.EmploymentRecordId));

        // The exception: resolved, never deleted, with the same fields.
        var resolvedS = await viaServices.Db.ServiceRegimeExceptions.AsNoTracking().SingleAsync(e => e.Id == exceptionS.Id);
        var resolvedP = await viaPatch.Db.ServiceRegimeExceptions.AsNoTracking().SingleAsync(e => e.Id == exceptionP.Id);
        Assert.Equal(
            (resolvedS.Status, resolvedS.ResolvedRegimeId, resolvedS.Resolution, resolvedS.ResolvedBy, resolvedS.ConflictNotes, resolvedS.ExceptionType),
            (resolvedP.Status, resolvedP.ResolvedRegimeId, resolvedP.Resolution, resolvedP.ResolvedBy, resolvedP.ConflictNotes, resolvedP.ExceptionType));
        Assert.Equal(ServiceRegimeExceptionStatus.Resolved, resolvedP.Status);
        Assert.Equal("تصنيف بذرة القبول: القانون 50/2004", resolvedP.Resolution);
        Assert.NotNull(resolvedP.ResolvedAt);
        Assert.NotNull(resolvedS.ResolvedAt);
    }

    [Fact]
    public async Task After_the_patch_no_employee_of_the_agencies_is_blocked_for_payroll()
    {
        var s = await StartAsync();
        var (withException, recordWithException) = await AddEmployeeAsync(s.Db, AgencyA, "5300000011");
        await OpenAsync(s, AgencyA, withException, recordWithException);
        var (noException, _) = await AddEmployeeAsync(s.Db, AgencyA, "5300000012");
        var (agencyB, _) = await AddEmployeeAsync(s.Db, AgencyB, "5300000013");
        foreach (var employee in new[] { withException, noException, agencyB })
            Assert.True(await BlockedAsync(s, employee.Id));   // the gate as the seed leaves it: every current employee blocked

        var result = await AcceptancePayrollRegimePatch.ApplyAsync(s.Db);

        Assert.Equal(new AcceptancePayrollRegimePatch.Result(3, 1), result);
        foreach (var employee in new[] { withException, noException, agencyB })
            Assert.False(await BlockedAsync(s, employee.Id));
        // One transition per record, in its own agency.
        Assert.Equal(3, await s.Db.EmploymentRegimeTransitions.CountAsync());
        Assert.Equal(1, await s.Db.EmploymentRegimeTransitions.CountAsync(t => t.TenantId == AgencyB && t.EmployeeId == agencyB.Id));
    }

    [Fact]
    public async Task It_leaves_alone_what_is_not_a_current_unclassified_record_of_an_agency()
    {
        var s = await StartAsync();
        var (ended, endedRecord) = await AddEmployeeAsync(s.Db, AgencyA, "5300000021", endDate: Hired.AddYears(1));
        var (classified, classifiedRecord) = await AddEmployeeAsync(s.Db, AgencyA, "5300000022", regime: "law53");
        // An open unclassified exception on a record that already has a regime (a test set it by SQL): not this patch's to resolve.
        var staleException = await OpenAsync(s, AgencyA, classified, classifiedRecord);
        var (review, reviewRecord) = await AddEmployeeAsync(s.Db, AgencyA, "5300000023");
        var manualReview = await OpenAsync(s, AgencyA, review, reviewRecord, ServiceRegimeExceptionType.ManualReview);
        var article159 = await OpenAsync(s, AgencyA, review, reviewRecord, ServiceRegimeExceptionType.Article159Conflict);
        var (foreign, foreignRecord) = await AddEmployeeAsync(s.Db, NotAnAgency, "5300000024");
        var foreignException = await OpenAsync(s, NotAnAgency, foreign, foreignRecord);
        var exceptionsBefore = await s.Db.ServiceRegimeExceptions.CountAsync();

        var result = await AcceptancePayrollRegimePatch.ApplyAsync(s.Db);

        // Only the one current record of the agency without a regime was classified; no exception was resolved by it.
        Assert.Equal(new AcceptancePayrollRegimePatch.Result(1, 0), result);
        Assert.Null((await s.Db.EmploymentRecords.AsNoTracking().SingleAsync(r => r.Id == endedRecord.Id)).LegalRegimeId);
        Assert.Equal("law53", (await s.Db.EmploymentRecords.AsNoTracking().SingleAsync(r => r.Id == classifiedRecord.Id)).LegalRegimeId);
        Assert.Equal("law50", (await s.Db.EmploymentRecords.AsNoTracking().SingleAsync(r => r.Id == reviewRecord.Id)).LegalRegimeId);
        Assert.Null((await s.Db.EmploymentRecords.AsNoTracking().SingleAsync(r => r.Id == foreignRecord.Id)).LegalRegimeId);

        // Nothing deleted, and every exception that is not this patch's is still open.
        Assert.Equal(exceptionsBefore, await s.Db.ServiceRegimeExceptions.CountAsync());
        foreach (var kept in new[] { staleException, manualReview, article159, foreignException })
            Assert.Equal(ServiceRegimeExceptionStatus.Open, (await s.Db.ServiceRegimeExceptions.AsNoTracking().SingleAsync(e => e.Id == kept.Id)).Status);
        // So the employee under manual review is still blocked: another exception type is a decision for a person.
        Assert.True(await BlockedAsync(s, review.Id));
        Assert.True(await BlockedAsync(s, foreign.Id));
        Assert.False(await BlockedAsync(s, ended.Id));
    }

    [Fact]
    public async Task A_second_run_does_nothing()
    {
        var s = await StartAsync();
        var (employee, record) = await AddEmployeeAsync(s.Db, AgencyA, "5300000031");
        await OpenAsync(s, AgencyA, employee, record);

        Assert.Equal(new AcceptancePayrollRegimePatch.Result(1, 1), await AcceptancePayrollRegimePatch.ApplyAsync(s.Db));
        var resolvedAt = (await s.Db.ServiceRegimeExceptions.AsNoTracking().SingleAsync()).ResolvedAt;

        Assert.Equal(new AcceptancePayrollRegimePatch.Result(0, 0), await AcceptancePayrollRegimePatch.ApplyAsync(s.Db));
        Assert.Equal(new AcceptancePayrollRegimePatch.Result(0, 0), await AcceptancePayrollRegimePatch.ApplyAsync(s.Db));

        Assert.Equal(1, await s.Db.EmploymentRegimeTransitions.CountAsync());   // the regime was applied once
        Assert.Equal(resolvedAt, (await s.Db.ServiceRegimeExceptions.AsNoTracking().SingleAsync()).ResolvedAt);
    }

    [Fact]
    public void The_acceptance_orchestrator_calls_the_patch_and_the_demo_never_does()
    {
        var root = HRManagement.TestSupport.RepoPaths.Root;
        var orchestrator = System.IO.File.ReadAllText(Path.Combine(root, "src", "Services", "HRCore", "HR.Core.Infrastructure", "Seeding", "AcceptanceSeedOrchestrator.cs"));
        Assert.Contains("AcceptancePayrollRegimePatch.ApplyAsync", orchestrator, StringComparison.Ordinal);

        var callers = Directory.EnumerateFiles(Path.Combine(root, "src", "Services"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => System.IO.File.ReadAllText(f).Contains("AcceptancePayrollRegimePatch.", StringComparison.Ordinal))
            .Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["AcceptanceSeedOrchestrator.cs"], callers);
    }
}
