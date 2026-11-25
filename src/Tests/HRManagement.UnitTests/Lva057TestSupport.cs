using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Attendance;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-57: an HR.Core database in memory (SQLite) with a small org tree for the attendance tests: an agency root, branch 1 with a
/// sub-unit, and branch 2. «Today» is fixed, so a test's dates do not move with the clock.
/// </summary>
internal sealed class Lva057Db : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<HRCoreDbContext> _options;

    public HRCoreDbContext Db { get; }
    public Guid Root { get; } = Guid.NewGuid();
    public Guid Branch1 { get; } = Guid.NewGuid();
    public Guid Branch1Sub { get; } = Guid.NewGuid();
    public Guid Branch2 { get; } = Guid.NewGuid();

    /// <summary>March 2025: Fridays 7, 14, 21, 28; Saturdays 1, 8, 15, 22, 29; 22 working days with no holiday.</summary>
    public static readonly DateOnly MarchStart = new(2025, 3, 1);
    public static readonly DateOnly MarchEnd = new(2025, 3, 31);

    /// <summary>The saves of the main context (<see cref="Db"/>): a test of «one save» reads it.</summary>
    public SaveCounter Saves { get; } = new();

    public FixedTime Clock { get; }
    public AttendanceCalendar Calendar { get; }
    public AttendanceLockService Locks { get; }
    public AttendanceEligibility Eligibility { get; }
    public AttendanceDayWriter Writer { get; }

    private Lva057Db(SqliteConnection connection, DateTimeOffset now, string[] holidays)
    {
        _connection = connection;
        _options = new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(connection).Options;
        Db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(connection).AddInterceptors(Saves).Options);
        Clock = new FixedTime(now);
        Calendar = new AttendanceCalendar(new AttendanceCalendarOptions { Holidays = [.. holidays] }, Clock);
        Locks = new AttendanceLockService(Db);
        Eligibility = new AttendanceEligibility(Db);
        Writer = new AttendanceDayWriter(Db, Locks, Calendar, Eligibility);
    }

    /// <param name="now">«Now»; by default noon UTC on 15 June 2025, after the test month.</param>
    public static async Task<Lva057Db> CreateAsync(DateTimeOffset? now = null, params string[] holidays)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var test = new Lva057Db(connection, now ?? new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero), holidays);
        await test.Db.Database.EnsureCreatedAsync();
        test.Db.OrgUnits.AddRange(
            new OrgUnit { Id = test.Root, Code = "ROOT", NameAr = "الجهة", MaterializedPath = $"/{test.Root:D}/" },
            new OrgUnit { Id = test.Branch1, Code = "BR1", NameAr = "الفرع الأول", ParentId = test.Root, MaterializedPath = $"/{test.Root:D}/{test.Branch1:D}/" },
            new OrgUnit
            {
                Id = test.Branch1Sub,
                Code = "BR1-S",
                NameAr = "دائرة في الفرع الأول",
                ParentId = test.Branch1,
                MaterializedPath = $"/{test.Root:D}/{test.Branch1:D}/{test.Branch1Sub:D}/"
            },
            new OrgUnit { Id = test.Branch2, Code = "BR2", NameAr = "الفرع الثاني", ParentId = test.Root, MaterializedPath = $"/{test.Root:D}/{test.Branch2:D}/" });
        await test.Db.SaveChangesAsync();
        return test;
    }

    /// <summary>Every attendance summary version staged for payroll through the test's publish endpoint, in order.</summary>
    public List<object> Published { get; } = [];

    private IPublishEndpoint? _publish;

    /// <summary>A publish endpoint that records what is staged (the bus outbox is the integration tests' matter).</summary>
    public IPublishEndpoint Publish
    {
        get
        {
            if (_publish is not null)
                return _publish;
            var mock = new Mock<IPublishEndpoint>(MockBehavior.Loose);
            mock.Setup(p => p.Publish<IAttendanceSummaryPublished>(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Callback<object, CancellationToken>((values, _) => Published.Add(values))
                .Returns(Task.CompletedTask);
            return _publish = mock.Object;
        }
    }

    /// <summary>A value of a staged message (the publisher passes an anonymous object).</summary>
    public static T Field<T>(object message, string name) => (T)message.GetType().GetProperty(name)!.GetValue(message)!;

    public static T? NullableField<T>(object message, string name) where T : struct => (T?)message.GetType().GetProperty(name)!.GetValue(message);

    public AttendanceOptions Options(
        AttendanceUnrecordedDaysMode unrecorded = AttendanceUnrecordedDaysMode.RequireComplete,
        AttendancePeriodMode periods = AttendancePeriodMode.CalendarMonth,
        AttendanceLeaveEventMode leaveEvents = AttendanceLeaveEventMode.AutoApply) => new()
        {
            Close = new AttendanceCloseOptions { UnrecordedWorkingDays = unrecorded },
            Periods = new AttendancePeriodsOptions { Mode = periods },
            Corrections = new AttendanceCorrectionsOptions { LeaveEventsAfterClose = leaveEvents }
        };

    public AttendancePeriodService PeriodService(
        AttendanceUnrecordedDaysMode unrecorded = AttendanceUnrecordedDaysMode.RequireComplete,
        AttendancePeriodMode periods = AttendancePeriodMode.CalendarMonth) =>
        new(Db, Publish, Eligibility, Calendar, Microsoft.Extensions.Options.Options.Create(Options(unrecorded, periods)));

    public AttendanceCorrectionService CorrectionService(
        AttendanceLeaveEventMode leaveEvents = AttendanceLeaveEventMode.AutoApply,
        AttendanceUnrecordedDaysMode unrecorded = AttendanceUnrecordedDaysMode.RequireComplete) =>
        new(Db, Writer, PeriodService(unrecorded), Eligibility, Calendar,
            Microsoft.Extensions.Options.Options.Create(Options(unrecorded, leaveEvents: leaveEvents)));

    /// <summary>What a leave decision does to the days, on the main context.</summary>
    public HR.Core.Infrastructure.Integration.AttendanceIntegrationService Integration(
        AttendanceLeaveEventMode leaveEvents = AttendanceLeaveEventMode.AutoApply) => new(Writer, CorrectionService(leaveEvents));

    /// <summary>The same services on another context (a redelivery's own scope).</summary>
    public HR.Core.Infrastructure.Integration.AttendanceIntegrationService IntegrationOn(HRCoreDbContext other)
    {
        var eligibility = new AttendanceEligibility(other);
        var otherWriter = new AttendanceDayWriter(other, new AttendanceLockService(other), Calendar, eligibility);
        var options = Microsoft.Extensions.Options.Options.Create(Options());
        var otherPeriods = new AttendancePeriodService(other, Publish, eligibility, Calendar, options);
        return new(otherWriter, new AttendanceCorrectionService(other, otherWriter, otherPeriods, eligibility, Calendar, options));
    }

    /// <summary>A second context on the same database: what is really stored, whatever the first context tracks.</summary>
    public HRCoreDbContext Fresh() => new(_options);

    public AttendanceActor Central(Guid? user = null) => new(user ?? Guid.NewGuid(), OrgAccessScope.Unrestricted());

    /// <summary>A branch manager of branch 1: his scope is the branch and its sub-unit.</summary>
    public AttendanceActor Branch1Manager(Guid? user = null) =>
        new(user ?? Guid.NewGuid(), OrgAccessScope.Restricted(new HashSet<Guid> { Branch1, Branch1Sub }));

    public async Task<Guid> EmployeeAsync(
        Guid? unit, string name = "موظف اختبار", DateTime? hire = null, EmploymentStatus status = EmploymentStatus.Active)
    {
        var id = Guid.NewGuid();
        Db.Employees.Add(new Employee
        {
            Id = id,
            FirstName = name,
            LastName = "الحضور",
            FullName = name + " الحضور",
            NationalId = "N" + id.ToString("N")[..10],
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = Gender.Male,
            CurrentStatus = status,
            PrimaryOrgUnitId = unit,
            HireDate = hire ?? new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await Db.SaveChangesAsync();
        return id;
    }

    /// <summary>Ends an employee's service at a moment: his status, and the history row the close reads the date from.</summary>
    public async Task EndServiceAsync(Guid employee, DateTime atUtc, EmploymentStatus status = EmploymentStatus.Terminated)
    {
        var entity = await Db.Employees.FirstAsync(e => e.Id == employee);
        var old = entity.CurrentStatus;
        entity.CurrentStatus = status;
        Db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = employee,
            OldStatus = old.ToString(),
            NewStatus = status.ToString(),
            ChangedAt = atUtc,
            Reason = "انتهاء الخدمة"
        });
        await Db.SaveChangesAsync();
    }

    /// <summary>Plants a record as stored data (a test's starting point, not a write of the product).</summary>
    public async Task<Guid> RecordAsync(
        Guid employee, DateOnly day, DailyAttendanceStatus status,
        AttendanceRecordSource source = AttendanceRecordSource.Manual, string? reference = null, DailyAttendanceStatus? replaced = null, string? notes = null)
    {
        await using var db = Fresh();
        var record = new DailyAttendanceRecord
        {
            EmployeeId = employee,
            AttendanceDate = day,
            Status = status,
            Source = source,
            SourceReference = reference,
            ReplacedStatus = replaced,
            Notes = notes
        };
        db.DailyAttendanceRecords.Add(record);
        await db.SaveChangesAsync();
        return record.Id;
    }

    /// <summary>Fills an employee's working days of a range as stored data.</summary>
    public async Task FillAsync(Guid employee, DateOnly from, DateOnly to, DailyAttendanceStatus status = DailyAttendanceStatus.Present)
    {
        await using var db = Fresh();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (Calendar.IsWorkingDay(day))
                db.DailyAttendanceRecords.Add(new DailyAttendanceRecord { EmployeeId = employee, AttendanceDate = day, Status = status, Source = AttendanceRecordSource.Bulk });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Locks an employee's month as stored data: a period and his summary in it (what a close leaves).</summary>
    public async Task<(Guid Period, Guid Summary)> LockAsync(Guid employee, DateOnly monthStart, int absentDays = 0, int workingDays = 22)
    {
        await using var db = Fresh();
        var end = monthStart.AddMonths(1).AddDays(-1);
        var period = await db.AttendancePeriods.FirstOrDefaultAsync(p => p.PeriodStart == monthStart);
        if (period is null)
        {
            period = new AttendancePeriod { PeriodStart = monthStart, PeriodEnd = end, Status = AttendancePeriodStatus.PartiallyClosed };
            db.AttendancePeriods.Add(period);
        }

        var summary = new AttendanceEmployeeSummary
        {
            AttendancePeriodId = period.Id,
            EmployeeId = employee,
            Version = 1,
            WorkingDays = workingDays,
            PresentDays = workingDays - absentDays,
            AbsentDays = absentDays,
            PublishedAt = DateTime.UtcNow
        };
        db.AttendanceEmployeeSummaries.Add(summary);
        await db.SaveChangesAsync();
        return (period.Id, summary.Id);
    }

    public async Task<DailyAttendanceRecord?> StoredAsync(Guid employee, DateOnly day)
    {
        await using var db = Fresh();
        return await db.DailyAttendanceRecords.AsNoTracking().FirstOrDefaultAsync(r => r.EmployeeId == employee && r.AttendanceDate == day);
    }

    public async Task<int> StoredCountAsync(Guid? employee = null)
    {
        await using var db = Fresh();
        return await db.DailyAttendanceRecords.CountAsync(r => employee == null || r.EmployeeId == employee);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public sealed class SaveCounter : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public int Count { get; private set; }

        /// <summary>When set, the next save fails before anything is written (an injected failure).</summary>
        public bool FailNext { get; set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("injected save failure");
            }

            Count++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    public sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
