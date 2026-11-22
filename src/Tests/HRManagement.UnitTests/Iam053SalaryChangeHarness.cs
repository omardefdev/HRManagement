using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Repositories;

namespace HRManagement.UnitTests;

/// <summary>PLAN-53: records the salary change events a unit of work staged, in order.</summary>
public sealed class Iam053RecordingEvents : IPayProfileChangeEventPublisher
{
    public List<object> Published { get; } = [];

    public Task PublishSubmittedAsync(PayProfileChangeSubmittedEvent message, CancellationToken ct)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public Task PublishDecidedAsync(PayProfileChangeDecidedEvent message, CancellationToken ct)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public Task PublishAppliedAsync(PayProfileChangeAppliedEvent message, CancellationToken ct)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>A clock a test moves.</summary>
public sealed class Iam053Clock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// PLAN-53: the salary change transaction on an in-memory database, as the handlers run it: one employee with an active profile that
/// holds a unit, a regime and a bank account, a grade with bounds, a processor and an approver, and a clock (15 November 2026, noon
/// UTC, unless a test says otherwise). Every send uses a fresh context, as a request does.
/// </summary>
public sealed class Iam053SalaryChangeHarness
{
    public static readonly Guid Employee = Guid.Parse("53000000-0000-4000-8000-0000000000e1");
    public static readonly Guid OtherEmployee = Guid.Parse("53000000-0000-4000-8000-0000000000e2");
    public static readonly Guid Unit = Guid.Parse("53000000-0000-4000-8000-0000000000a1");
    public static readonly Guid ForeignUnit = Guid.Parse("53000000-0000-4000-8000-0000000000a2");
    public static readonly Guid Processor = Guid.Parse("53000000-0000-4000-8000-0000000000c1");
    public static readonly Guid OtherProcessor = Guid.Parse("53000000-0000-4000-8000-0000000000c2");
    public static readonly Guid Approver = Guid.Parse("53000000-0000-4000-8000-0000000000c3");

    public const decimal Salary = 4500m;
    public const decimal GradeMin = 4000m;
    public const decimal GradeMax = 6000m;
    public const string Reason = "ترفيع استثنائي بقرار الوزير";
    public const string LegalReference = "القرار 112 لعام 2026";

    private readonly string _database = Guid.NewGuid().ToString();

    public Iam053SalaryChangeHarness(DateTimeOffset? now = null)
    {
        Clock = new Iam053Clock(now ?? new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
    }

    public Iam053Clock Clock { get; }

    public Iam053RecordingEvents Events { get; } = new();

    public PayProfileChangeOptions Options { get; } = new();

    public Guid GradeId { get; private set; }

    public Guid HigherGradeId { get; private set; }

    public Guid ProfileId { get; private set; }

    public PayrollDbContext Db() =>
        new(new DbContextOptionsBuilder<PayrollDbContext>().UseInMemoryDatabase(_database).Options);

    public IPayrollCalendar Calendar => new PayrollCalendar(Clock);

    public PayProfileChangeRules Rules(PayrollDbContext db) =>
        new(new PayrollRepository(db), Calendar, Microsoft.Extensions.Options.Options.Create(Options));

    public PayProfileChangeApplier Applier(PayrollDbContext db) =>
        new(new PayrollRepository(db), Events, Rules(db), Calendar);

    public PayProfileChangeActivator Activator(PayrollDbContext db) =>
        new(new PayrollRepository(db), Applier(db), Calendar, NullLogger<PayProfileChangeActivator>.Instance);

    /// <summary>The seed: a scale, two grades and the employee's active profile from 1 January 2026.</summary>
    public async Task<Iam053SalaryChangeHarness> SeedAsync(Guid? orgUnitId = null, bool withUnit = true)
    {
        await using var db = Db();
        var scale = new SalaryScale { Code = "S-2026", NameAr = "سلم 2026", EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var grade = new PayGrade { SalaryScale = scale, GradeCode = "G5", GradeNameAr = "الخامسة", BaseSalary = 4500, MinSalary = GradeMin, MaxSalary = GradeMax };
        var higher = new PayGrade { SalaryScale = scale, GradeCode = "G6", GradeNameAr = "السادسة", BaseSalary = 6500, MinSalary = 6000, MaxSalary = 8000 };
        var profile = new EmployeePayProfile
        {
            EmployeeId = Employee,
            PayGrade = grade,
            BaseSalary = Salary,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true,
            OrgUnitId = withUnit ? orgUnitId ?? Unit : null,
            RegimeCode = "law50",
            BankAccountIban = "SY0000000000000000000053",
            BankName = "المصرف التجاري السوري",
            BankAccountVerifiedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        db.AddRange(scale, grade, higher, profile);
        await db.SaveChangesAsync();
        (GradeId, HigherGradeId, ProfileId) = (grade.Id, higher.Id, profile.Id);
        return this;
    }

    // --- the commands, each in its own unit of work ---

    public async Task<PayProfileChangeResult> SubmitAsync(
        decimal? salary = 5000m,
        Guid? grade = null,
        DateOnly? effectiveFrom = null,
        string? reason = Reason,
        string? legalReference = LegalReference,
        PayProfileChangeSource source = PayProfileChangeSource.Manual,
        Guid? employee = null,
        Guid? actor = null,
        OrgAccessScope? scope = null)
    {
        await using var db = Db();
        return await new SubmitPayProfileChangeHandler(new PayrollRepository(db), Events, Rules(db), Calendar).Handle(
            new SubmitPayProfileChangeCommand(
                employee ?? Employee, salary, grade, effectiveFrom ?? Calendar.FirstDayOfCurrentMonth, reason, legalReference, source, null,
                actor ?? Processor, "processor", scope),
            CancellationToken.None);
    }

    public async Task<PayProfileChangeResult> DecideAsync(
        Guid requestId, PayProfileChangeDecision decision, string? note = null, Guid? actor = null, OrgAccessScope? scope = null)
    {
        await using var db = Db();
        return await new DecidePayProfileChangeHandler(new PayrollRepository(db), Events, Applier(db), Calendar).Handle(
            new DecidePayProfileChangeCommand(requestId, decision, note, actor ?? Approver, "approver", scope), CancellationToken.None);
    }

    public Task<PayProfileChangeResult> ApproveAsync(Guid requestId, Guid? actor = null) =>
        DecideAsync(requestId, PayProfileChangeDecision.Approved, actor: actor);

    public async Task<PayProfileChangeResult> CancelAsync(
        Guid requestId, string? reason = "خطأ في الإدخال", Guid? actor = null, bool actorCanApprove = false, OrgAccessScope? scope = null)
    {
        await using var db = Db();
        return await new CancelPayProfileChangeHandler(new PayrollRepository(db), Calendar).Handle(
            new CancelPayProfileChangeCommand(requestId, reason, actor ?? Processor, "user", actorCanApprove, scope), CancellationToken.None);
    }

    public async Task<PayProfileChangeActivationSummary> ActivateAsync()
    {
        await using var db = Db();
        return await Activator(db).ActivateDueAsync();
    }

    public async Task<PayProfileChangeResult> SystemChangeAsync(
        PayProfileChangeSource source, PayProfileChangeKind kind = PayProfileChangeKind.BaseSalary, decimal? salary = 5200m, Guid? grade = null,
        DateOnly? effectiveFrom = null, string? reference = "قرار 77/2026")
    {
        await using var db = Db();
        return await Applier(db).ApplySystemChangeAsync(
            Employee, kind, source, reference, salary, grade, effectiveFrom ?? Calendar.FirstDayOfCurrentMonth);
    }

    // --- reads ---

    public async Task<PayProfileChangeRequest> RequestAsync(Guid id)
    {
        await using var db = Db();
        return await db.PayProfileChangeRequests.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    public async Task<List<EmployeePayProfile>> ProfilesAsync(Guid? employee = null)
    {
        await using var db = Db();
        var id = employee ?? Employee;
        return await db.EmployeePayProfiles.AsNoTracking().Where(p => p.EmployeeId == id).OrderBy(p => p.EffectiveFrom).ToListAsync();
    }

    public async Task<EmployeePayProfile> ActiveAsync() => (await ProfilesAsync()).Single(p => p.IsActive);

    public async Task<int> RequestCountAsync()
    {
        await using var db = Db();
        return await db.PayProfileChangeRequests.CountAsync();
    }

    public async Task<int> HistoryCountAsync()
    {
        await using var db = Db();
        return await db.EmployeePayProfileHistories.CountAsync();
    }

    /// <summary>Nothing of the pay profile changed: the one seeded version, active, with its salary, and no history row.</summary>
    public async Task AssertProfileUntouchedAsync()
    {
        var profile = Assert.Single(await ProfilesAsync());
        Assert.Equal(ProfileId, profile.Id);
        Assert.True(profile.IsActive);
        Assert.Equal(Salary, profile.BaseSalary);
        Assert.Equal(GradeId, profile.PayGradeId);
        Assert.Null(profile.EffectiveTo);
        Assert.Equal(0, await HistoryCountAsync());
    }

    /// <summary>A run of the month of <paramref name="month"/> in a given state, with or without a line of the employee.</summary>
    public async Task<Guid> AddRunAsync(
        DateOnly month, PayrollRunType type, PayrollRunStatus status, bool frozen, bool withEmployeeLine = false)
    {
        await using var db = Db();
        var start = new DateTime(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var run = new PayrollRun
        {
            PeriodStart = start,
            PeriodEnd = start.AddMonths(1).AddDays(-1),
            RunDate = start,
            RunType = type,
            Status = status,
            ResultsFrozen = frozen,
        };
        db.PayrollRuns.Add(run);
        if (withEmployeeLine)
            db.SalaryLines.Add(new SalaryLine { PayrollRun = run, EmployeeId = Employee, GrossPay = 1, NetPay = 1 });
        await db.SaveChangesAsync();
        return run.Id;
    }

    public async Task SetRunStatusAsync(Guid runId, PayrollRunStatus status)
    {
        await using var db = Db();
        (await db.PayrollRuns.SingleAsync(r => r.Id == runId)).Status = status;
        await db.SaveChangesAsync();
    }

    public async Task SetGradeBoundsAsync(Guid gradeId, decimal min, decimal max)
    {
        await using var db = Db();
        var grade = await db.PayGrades.SingleAsync(g => g.Id == gradeId);
        (grade.MinSalary, grade.MaxSalary) = (min, max);
        await db.SaveChangesAsync();
    }

    public static OrgAccessScope Restricted(params Guid[] units) => OrgAccessScope.Restricted(units.ToHashSet());
}
