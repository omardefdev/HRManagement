using HRManagement.SharedKernel.Entities;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payroll.Application.Features.PayProfileRepair;
using Payroll.Domain.Entities;
using Payroll.Infrastructure;
using Payroll.Infrastructure.Data;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-55 (P-8): the repair of old pay profiles. A dry run changes nothing and lists what a real run then changes, exactly; nothing
/// is overwritten; a second run finds nothing; a value nobody of the employee's own history carries is never invented.
/// </summary>
public sealed class Pay055ProfileRepairTests : IAsyncDisposable
{
    private static readonly Guid Operator = Guid.Parse("55000000-0000-4000-8000-0000000000d1");
    private static readonly Guid UnitA = Guid.Parse("55000000-0000-4000-8000-0000000000a1");
    private static readonly Guid UnitB = Guid.Parse("55000000-0000-4000-8000-0000000000a2");

    private readonly ServiceProvider _root;

    public Pay055ProfileRepairTests()
    {
        // The service's own registrations on an in-memory database: the repair dispatches the named writer through MediatR.
        var services = new ServiceCollection();
        services.AddLogging();
        var database = Guid.NewGuid().ToString();
        // The repair runs in one transaction; the in-memory store has none (the transaction itself is proved on PostgreSQL).
        services.AddDbContext<PayrollDbContext>(o => o.UseInMemoryDatabase(database)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddPayrollInfrastructure();
        services.AddSingleton(Mock.Of<IPublishEndpoint>());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _root = services.BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static EmployeePayProfile Profile(Guid employee, bool active, DateTime from, Guid? unit, string? regime, decimal salary = 4500m) => new()
    {
        EmployeeId = employee,
        IsActive = active,
        EffectiveFrom = from,
        EffectiveTo = active ? null : from.AddMonths(6).AddDays(-1),
        OrgUnitId = unit,
        RegimeCode = regime,
        BaseSalary = salary,
    };

    private async Task SeedAsync(params EmployeePayProfile[] profiles)
    {
        await using var scope = _root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        db.AddRange(profiles);
        await db.SaveChangesAsync();
    }

    private async Task<ProfileOrgUnitBackfillReport> RunAsync(bool dryRun, IReadOnlyCollection<Guid>? only = null)
    {
        await using var scope = _root.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new BackfillProfileOrgUnitCommand(dryRun, Operator, "ops-admin", only));
    }

    private async Task<List<(Guid Id, Guid? Unit, string? Regime, decimal Salary, bool Active, DateTime Updated)>> SnapshotAsync()
    {
        await using var scope = _root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        return (await db.EmployeePayProfiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync())
            .Select(p => (p.Id, p.OrgUnitId, p.RegimeCode, p.BaseSalary, p.IsActive, p.UpdatedAt)).ToList();
    }

    private async Task<List<EntityAuditLog>> RepairAuditsAsync()
    {
        await using var scope = _root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        return await db.AuditLogs.AsNoTracking().Where(a => a.Action == BackfillProfileOrgUnitHandler.AuditAction).ToListAsync();
    }

    [Fact]
    public async Task A_dry_run_changes_nothing_and_a_real_run_changes_exactly_what_it_listed()
    {
        var (both, unitOnly, regimeOnly, complete, hopeless) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var bothActive = Profile(both, true, Utc(2026, 6, 1), null, null);
        var bothOld = Profile(both, false, Utc(2025, 1, 1), UnitA, "law50");
        var unitActive = Profile(unitOnly, true, Utc(2026, 6, 1), null, "contract");
        var unitOld = Profile(unitOnly, false, Utc(2025, 1, 1), UnitB, "law50");
        var regimeActive = Profile(regimeOnly, true, Utc(2026, 6, 1), UnitB, "  ");
        var regimeOld = Profile(regimeOnly, false, Utc(2025, 1, 1), UnitA, " law50 ");
        var completeActive = Profile(complete, true, Utc(2026, 6, 1), UnitA, "law50");
        var hopelessActive = Profile(hopeless, true, Utc(2026, 6, 1), UnitA, null);          // no profile of his carries a regime
        var hopelessOld = Profile(hopeless, false, Utc(2025, 1, 1), UnitB, null);
        await SeedAsync(bothActive, bothOld, unitActive, unitOld, regimeActive, regimeOld, completeActive, hopelessActive, hopelessOld);
        var before = await SnapshotAsync();

        // The dry run: the default, lists and changes nothing.
        var dry = await RunAsync(dryRun: true);

        Assert.True(dry.DryRun);
        Assert.Equal((2, 3, 3, 0), (dry.ActiveProfilesWithEmptyUnit, dry.ActiveProfilesWithEmptyRegime, dry.Found, dry.Changed));
        Assert.Null(dry.AuditEntryId);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Empty(await RepairAuditsAsync());
        Assert.Equal(
            new[]
            {
                new ProfileOrgUnitBackfillItem(bothActive.Id, both, UnitA, bothOld.Id, "law50", bothOld.Id),
                new ProfileOrgUnitBackfillItem(unitActive.Id, unitOnly, UnitB, unitOld.Id, null, null),          // its own regime stays
                new ProfileOrgUnitBackfillItem(regimeActive.Id, regimeOnly, null, null, "law50", regimeOld.Id),  // its own unit stays
            }.OrderBy(i => i.ProfileId),
            dry.Items.OrderBy(i => i.ProfileId));
        Assert.Equal(new[] { hopelessActive.Id }, dry.NotFillableProfileIds);

        // The real run: exactly the listed profiles, exactly the listed values, and one audit entry.
        var real = await RunAsync(dryRun: false);

        Assert.False(real.DryRun);
        Assert.Equal((3, 3), (real.Found, real.Changed));
        Assert.Equal(dry.Items.OrderBy(i => i.ProfileId), real.Items.OrderBy(i => i.ProfileId));
        var after = (await SnapshotAsync()).ToDictionary(p => p.Id);
        Assert.Equal((UnitA, "law50"), (after[bothActive.Id].Unit, after[bothActive.Id].Regime));
        Assert.Equal((UnitB, "contract"), (after[unitActive.Id].Unit, after[unitActive.Id].Regime));       // never overwritten
        Assert.Equal((UnitB, "law50"), (after[regimeActive.Id].Unit, after[regimeActive.Id].Regime));      // never overwritten
        // Nothing else moved: not a salary, not an inactive profile, not the complete one, not the one that cannot be filled.
        var untouched = new[] { bothOld.Id, unitOld.Id, regimeOld.Id, completeActive.Id, hopelessActive.Id, hopelessOld.Id };
        Assert.Equal(before.Where(p => untouched.Contains(p.Id)), after.Values.Where(p => untouched.Contains(p.Id)).OrderBy(p => p.Id));
        Assert.All(after.Values, p => Assert.Equal(4500m, p.Salary));
        Assert.Equal(before.Select(p => (p.Id, p.Active)), after.Values.OrderBy(p => p.Id).Select(p => (p.Id, p.Active)));
        var audit = Assert.Single(await RepairAuditsAsync());
        Assert.Equal((real.AuditEntryId, (Guid?)Operator, nameof(EmployeePayProfile)), ((Guid?)audit.Id, audit.UserId, audit.EntityType));
        Assert.Contains("\"changed\":3", audit.ChangedFields, StringComparison.Ordinal);
        Assert.Contains(bothActive.Id.ToString(), audit.ChangedFields, StringComparison.OrdinalIgnoreCase);

        // Idempotent: a second run finds nothing, changes nothing and writes no second audit entry.
        var settled = await SnapshotAsync();
        var again = await RunAsync(dryRun: false);
        Assert.Equal((0, 0, 0, 1), (again.Found, again.Changed, again.ActiveProfilesWithEmptyUnit, again.ActiveProfilesWithEmptyRegime));
        Assert.Equal(new[] { hopelessActive.Id }, again.NotFillableProfileIds);
        Assert.Equal(settled, await SnapshotAsync());
        Assert.Single(await RepairAuditsAsync());
    }

    [Fact]
    public async Task The_value_comes_from_the_employees_own_latest_profile_that_carries_it_and_from_nobody_else()
    {
        var (employee, other) = (Guid.NewGuid(), Guid.NewGuid());
        var active = Profile(employee, true, Utc(2026, 6, 1), null, null);
        var oldest = Profile(employee, false, Utc(2023, 1, 1), UnitA, "old");
        var middle = Profile(employee, false, Utc(2024, 1, 1), UnitB, null);            // the latest that carries a unit
        var latest = Profile(employee, false, Utc(2025, 1, 1), null, "law50");          // the latest that carries a regime
        // Another employee with a complete history: never a source for the first.
        var othersActive = Profile(other, true, Utc(2026, 6, 1), UnitA, "contract");
        var loner = Profile(Guid.NewGuid(), true, Utc(2026, 6, 1), null, null);         // no history at all
        await SeedAsync(active, oldest, middle, latest, othersActive, loner);

        var real = await RunAsync(dryRun: false);

        var item = Assert.Single(real.Items);
        Assert.Equal(new ProfileOrgUnitBackfillItem(active.Id, employee, UnitB, middle.Id, "law50", latest.Id), item);
        Assert.Equal(new[] { loner.Id }, real.NotFillableProfileIds);
        var filled = (await SnapshotAsync()).Single(p => p.Id == active.Id);
        Assert.Equal((UnitB, "law50"), (filled.Unit, filled.Regime));
        var untouched = (await SnapshotAsync()).Single(p => p.Id == loner.Id);
        Assert.Equal(((Guid?)null, (string?)null), (untouched.Unit, untouched.Regime));
    }

    [Fact]
    public async Task An_employee_with_no_active_profile_is_not_repaired()
    {
        var employee = Guid.NewGuid();
        await SeedAsync(Profile(employee, false, Utc(2025, 1, 1), null, null), Profile(employee, false, Utc(2024, 1, 1), UnitA, "law50"));
        var before = await SnapshotAsync();

        var real = await RunAsync(dryRun: false);

        Assert.Equal((0, 0, 0, 0), (real.Found, real.Changed, real.ActiveProfilesWithEmptyUnit, real.NotFillableProfileIds.Count));
        Assert.Equal(before, await SnapshotAsync());
        Assert.Empty(await RepairAuditsAsync());
    }

    [Fact]
    public async Task A_real_run_given_the_dry_runs_list_changes_nothing_outside_it()
    {
        var (listed, late) = (Guid.NewGuid(), Guid.NewGuid());
        var listedActive = Profile(listed, true, Utc(2026, 6, 1), null, null);
        await SeedAsync(listedActive, Profile(listed, false, Utc(2025, 1, 1), UnitA, "law50"));
        var dry = await RunAsync(dryRun: true);
        Assert.Equal(new[] { listedActive.Id }, dry.Items.Select(i => i.ProfileId));

        // Another employee becomes fillable after the dry run.
        var lateActive = Profile(late, true, Utc(2026, 6, 1), null, null);
        await SeedAsync(lateActive, Profile(late, false, Utc(2025, 1, 1), UnitB, "contract"));

        var real = await RunAsync(dryRun: false, only: dry.Items.Select(i => i.ProfileId).ToList());

        Assert.Equal((1, 1), (real.Found, real.Changed));
        Assert.Equal(new[] { lateActive.Id }, real.NotInTheDryRun);
        var after = (await SnapshotAsync()).ToDictionary(p => p.Id);
        Assert.Equal((UnitA, "law50"), (after[listedActive.Id].Unit, after[listedActive.Id].Regime));
        Assert.Equal(((Guid?)null, (string?)null), (after[lateActive.Id].Unit, after[lateActive.Id].Regime));

        // A list that names nothing fillable changes nothing and writes no audit entry beyond the first run's.
        var none = await RunAsync(dryRun: false, only: [Guid.NewGuid()]);
        Assert.Equal((0, 0), (none.Found, none.Changed));
        Assert.Single(await RepairAuditsAsync());
    }

    [Theory]
    [InlineData(Payroll.Domain.Enums.PayrollRunJobStatus.Pending)]
    [InlineData(Payroll.Domain.Enums.PayrollRunJobStatus.Running)]
    [InlineData(Payroll.Domain.Enums.PayrollRunJobStatus.Paused)]
    public async Task A_real_run_is_refused_while_a_calculation_is_in_progress_and_a_dry_run_is_not(Payroll.Domain.Enums.PayrollRunJobStatus status)
    {
        var employee = Guid.NewGuid();
        var active = Profile(employee, true, Utc(2026, 6, 1), null, null);
        await SeedAsync(active, Profile(employee, false, Utc(2025, 1, 1), UnitA, "law50"));
        await using (var scope = _root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            db.Add(new PayrollRunJob { PayrollRunId = Guid.NewGuid(), Status = status });
            await db.SaveChangesAsync();
        }

        var before = await SnapshotAsync();

        var dry = await RunAsync(dryRun: true);
        var real = await RunAsync(dryRun: false);

        Assert.Equal((1, (string?)null), (dry.Found, dry.Refusal));
        Assert.Equal((BackfillProfileOrgUnitHandler.CalculationInProgress, 0), (real.Refusal, real.Changed));
        Assert.Equal(before, await SnapshotAsync());
        Assert.Empty(await RepairAuditsAsync());
    }
}
