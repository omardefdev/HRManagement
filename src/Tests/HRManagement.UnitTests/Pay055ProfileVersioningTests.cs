using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.InboundEvents;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Options;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Repositories;
using H = HRManagement.UnitTests.Iam053SalaryChangeHarness;
using V = Payroll.Domain.Services.EmployeePayProfileVersioning;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-55 (§3): no new pay profile version loses the organisational unit or the legal regime, whatever made it. The rules of
/// <c>CreateNextVersion</c>; the contract activation through PLAN-53's applier (a first activation, a version over the one in force,
/// PAY-029 kept, a message delivered twice, a refusal consumed with its hold); and a late <c>IEmployeeCreated</c>.
/// </summary>
public sealed class Pay055ProfileVersioningTests
{
    private static readonly Guid Unit = H.Unit;
    private static readonly Guid OtherUnit = H.ForeignUnit;
    private static readonly Guid NewHire = H.OtherEmployee;
    private const string ContractType = "ACC-LAW50-PERM";

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static EmployeePayProfile Profile(
        bool active, DateTime from, DateTime? to = null, decimal salary = 4500m, Guid? unit = null, string? regime = "law50", Guid? grade = null) => new()
        {
            EmployeeId = NewHire,
            TenantId = Guid.Parse("55000000-0000-4000-8000-00000000000a"),
            IsActive = active,
            EffectiveFrom = from,
            EffectiveTo = to,
            BaseSalary = salary,
            OrgUnitId = unit ?? Unit,
            RegimeCode = regime,
            PayGradeId = grade,
        };

    // --- CreateNextVersion ---

    [Fact]
    public void With_no_profile_it_makes_the_first_one_from_what_the_source_gives()
    {
        var step = V.CreateNextVersion(NewHire, [], new PayProfileVersionChange(
            new DateOnly(2026, 12, 1), OrgUnitId: Unit, RegimeCode: " law50 ", IsActive: false));

        Assert.Null(step.Previous);
        Assert.Null(step.History);
        Assert.False(step.PreviousWasInForce);
        Assert.Equal(NewHire, step.Next.EmployeeId);
        Assert.Equal((Unit, "law50"), (step.Next.OrgUnitId, step.Next.RegimeCode));
        Assert.Equal((0m, (Guid?)null, false), (step.Next.BaseSalary, step.Next.PayGradeId, step.Next.IsActive));
        // A calendar date, stored at 00:00 UTC of that date.
        Assert.Equal(Utc(2026, 12, 1), step.Next.EffectiveFrom);
        Assert.Equal(DateTimeKind.Utc, step.Next.EffectiveFrom.Kind);
        Assert.Null(step.Next.EffectiveTo);
    }

    [Fact]
    public void From_the_profile_in_force_it_carries_everything_and_closes_it_the_day_before()
    {
        var grade = Guid.NewGuid();
        var inForce = Profile(active: true, Utc(2026, 1, 1), grade: grade);
        var older = Profile(active: false, Utc(2025, 1, 1), Utc(2025, 12, 31), unit: OtherUnit, regime: "old");

        var step = V.CreateNextVersion(NewHire, [older, inForce], new PayProfileVersionChange(
            new DateOnly(2026, 12, 1), BaseSalary: 5200m, HistoryReason: "تفعيل عقد"));

        Assert.Same(inForce, step.Previous);
        Assert.True(step.PreviousWasInForce);
        // The unit, the regime, the grade and the agency of the version in force; the salary of the change.
        Assert.Equal((Unit, "law50", grade, inForce.TenantId), (step.Next.OrgUnitId, step.Next.RegimeCode, step.Next.PayGradeId, step.Next.TenantId));
        Assert.Equal((5200m, true), (step.Next.BaseSalary, step.Next.IsActive));
        // No day in two versions: the previous one ends on 30 November, the next starts on 1 December.
        Assert.False(inForce.IsActive);
        Assert.Equal(Utc(2026, 11, 30), inForce.EffectiveTo);
        Assert.Equal(Utc(2026, 12, 1), step.Next.EffectiveFrom);
        Assert.NotEqual(inForce.EffectiveTo, step.Next.EffectiveFrom);
        // Its history snapshot.
        Assert.NotNull(step.History);
        Assert.Equal((inForce.Id, "تفعيل عقد", 4500m), (step.History!.SourceProfileId, step.History.ChangeReason, step.History.BaseSalary));
        // The older, closed version is not touched.
        Assert.Equal(Utc(2025, 12, 31), older.EffectiveTo);
    }

    [Fact]
    public void A_missing_salary_or_grade_keeps_that_of_the_version_in_force()
    {
        var grade = Guid.NewGuid();
        var inForce = Profile(active: true, Utc(2026, 1, 1), grade: grade);

        var step = V.CreateNextVersion(NewHire, [inForce], new PayProfileVersionChange(new DateOnly(2026, 12, 1)));

        Assert.Equal((4500m, grade), (step.Next.BaseSalary, step.Next.PayGradeId));
    }

    [Fact]
    public void With_no_profile_in_force_it_starts_from_the_latest_one_and_carries_its_unit_and_regime()
    {
        // The inactive, unpriced profile IEmployeeCreated wrote: the first contract activation used to ignore it.
        var placeholder = Profile(active: false, Utc(2026, 11, 20), salary: 0m);

        var step = V.CreateNextVersion(NewHire, [placeholder], new PayProfileVersionChange(
            new DateOnly(2026, 12, 1), BaseSalary: 5000m, HistoryReason: "تفعيل عقد"));

        Assert.Same(placeholder, step.Previous);
        Assert.False(step.PreviousWasInForce);
        Assert.Equal((Unit, "law50", 5000m, true), (step.Next.OrgUnitId, step.Next.RegimeCode, step.Next.BaseSalary, step.Next.IsActive));
        Assert.Equal(Utc(2026, 12, 1), step.Next.EffectiveFrom);
        // The placeholder is closed the day before, and snapshot.
        Assert.Equal(Utc(2026, 11, 30), placeholder.EffectiveTo);
        Assert.False(placeholder.IsActive);
        Assert.NotNull(step.History);
    }

    [Fact]
    public void A_placeholder_never_held_a_day_so_a_version_replaces_it_whatever_its_date()
    {
        // The employee was created on 20 November; his contract started on 1 November.
        var placeholder = Profile(active: false, Utc(2026, 11, 20), salary: 0m);
        Assert.True(V.IsPlaceholder(placeholder));
        Assert.False(V.StartsInside(new DateOnly(2026, 11, 1), placeholder));

        var step = V.CreateNextVersion(NewHire, [placeholder], new PayProfileVersionChange(new DateOnly(2026, 11, 1), BaseSalary: 5000m));

        Assert.Equal(Utc(2026, 11, 1), step.Next.EffectiveFrom);
        // Its end is before its own start: a version that holds no day, kept in the record.
        Assert.Equal(Utc(2026, 10, 31), placeholder.EffectiveTo);
        Assert.True(placeholder.EffectiveTo < placeholder.EffectiveFrom);
    }

    [Fact]
    public void The_salary_and_the_grade_are_carried_only_from_a_version_in_force()
    {
        // A closed version's salary and grade ended with it (a re-hire): the new version takes the source's, else 0 and no grade.
        var closed = Profile(active: false, Utc(2024, 1, 1), Utc(2025, 6, 30), salary: 3900m, grade: Guid.NewGuid());
        var grade = Guid.NewGuid();

        var unpriced = V.CreateNextVersion(NewHire, [closed], new PayProfileVersionChange(new DateOnly(2026, 12, 1), IsActive: false));
        var priced = V.CreateNextVersion(NewHire, [closed], new PayProfileVersionChange(new DateOnly(2026, 12, 1), 5000m, grade));

        Assert.Equal((0m, (Guid?)null, false), (unpriced.Next.BaseSalary, unpriced.Next.PayGradeId, unpriced.Next.IsActive));
        Assert.Equal((5000m, grade), (priced.Next.BaseSalary, priced.Next.PayGradeId));
        // The unit and the regime are the employee's: carried.
        Assert.Equal((Unit, "law50"), (unpriced.Next.OrgUnitId, unpriced.Next.RegimeCode));
    }

    [Fact]
    public void A_version_that_was_already_closed_keeps_its_end_and_gets_no_second_snapshot()
    {
        var closed = Profile(active: false, Utc(2024, 1, 1), Utc(2025, 6, 30));

        var step = V.CreateNextVersion(NewHire, [closed], new PayProfileVersionChange(new DateOnly(2026, 12, 1), BaseSalary: 5000m));

        Assert.Equal(Utc(2025, 6, 30), closed.EffectiveTo);
        Assert.Null(step.History);
        Assert.Null(step.Next.EffectiveTo);            // a closed version's end is no planned end of the new one
        Assert.False(V.IsPlaceholder(closed));
    }

    [Fact]
    public void The_latest_profile_is_the_one_that_starts_last()
    {
        var first = Profile(active: false, Utc(2024, 1, 1), Utc(2024, 12, 31), unit: OtherUnit, regime: "old");
        var last = Profile(active: false, Utc(2025, 1, 1), Utc(2025, 12, 31), unit: Unit, regime: "law50");

        Assert.Same(last, V.PreviousOf([first, last]));
        Assert.Same(last, V.PreviousOf([last, first]));
        Assert.Null(V.PreviousOf([]));
        // A profile in force wins over a later inactive one.
        var inForce = Profile(active: true, Utc(2023, 1, 1));
        Assert.Same(inForce, V.PreviousOf([first, last, inForce]));
    }

    [Fact]
    public void A_source_that_gives_a_unit_or_a_regime_replaces_the_carried_one()
    {
        var inForce = Profile(active: true, Utc(2026, 1, 1));

        var step = V.CreateNextVersion(NewHire, [inForce], new PayProfileVersionChange(
            new DateOnly(2026, 12, 1), OrgUnitId: OtherUnit, RegimeCode: "contract"));
        var kept = V.CreateNextVersion(NewHire, [Profile(active: true, Utc(2026, 1, 1))], new PayProfileVersionChange(
            new DateOnly(2026, 12, 1), RegimeCode: "  "));

        Assert.Equal((OtherUnit, "contract"), (step.Next.OrgUnitId, step.Next.RegimeCode));
        Assert.Equal((Unit, "law50"), (kept.Next.OrgUnitId, kept.Next.RegimeCode));     // a blank regime is no regime given
    }

    [Fact]
    public void The_planned_end_of_the_version_in_force_is_carried_and_no_other()
    {
        var planned = Profile(active: true, Utc(2026, 1, 1), Utc(2027, 6, 30));

        var step = V.CreateNextVersion(NewHire, [planned], new PayProfileVersionChange(new DateOnly(2026, 12, 1)));

        Assert.Equal(Utc(2027, 6, 30), step.Next.EffectiveTo);
        Assert.Equal(Utc(2026, 11, 30), planned.EffectiveTo);
    }

    [Theory]
    [InlineData(true, "2025-12-31", true)]      // before the start of the version in force
    [InlineData(true, "2026-01-01", false)]     // on its first day: it is replaced and holds no day
    [InlineData(true, "2026-06-01", false)]
    public void A_version_takes_no_day_before_the_start_of_the_version_in_force(bool active, string from, bool inside)
    {
        Assert.Equal(inside, V.StartsInside(DateOnly.Parse(from), Profile(active, Utc(2026, 1, 1))));
    }

    [Theory]
    [InlineData("2025-06-29", true)]
    [InlineData("2025-06-30", true)]            // the last day of the closed version is its own
    [InlineData("2025-07-01", false)]
    public void A_version_takes_no_day_of_a_closed_version(string from, bool inside)
    {
        Assert.Equal(inside, V.StartsInside(DateOnly.Parse(from), Profile(active: false, Utc(2024, 1, 1), Utc(2025, 6, 30))));
    }

    // --- the consumers ---

    private static async Task ActivateAsync(
        H h, Guid employee, DateTime start, decimal? salary, string? grade,
        Guid? contract = null, bool requireSalary = false, IPayProfileChangeApplier? applier = null)
    {
        await using var db = h.Db();
        await new HandleContractActivatedHandler(
                new PayrollRepository(db),
                Microsoft.Extensions.Options.Options.Create(new PayrollActivationOptions { RequireBaseSalaryOnActivation = requireSalary }),
                null,
                applier ?? h.Applier(db))
            .Handle(new HandleContractActivatedCommand(contract ?? Guid.NewGuid(), employee, ContractType, start, salary, grade), CancellationToken.None);
    }

    private static async Task CreatedAsync(H h, Guid employee, Guid? unit, string? regime)
    {
        await using var db = h.Db();
        await new HandleEmployeeCreatedHandler(new PayrollRepository(db), h.Calendar)
            .Handle(new HandleEmployeeCreatedCommand(employee, unit, regime), CancellationToken.None);
    }

    private static async Task<List<EmployeePayProfile>> ProfilesAsync(H h, Guid employee)
    {
        await using var db = h.Db();
        return await db.EmployeePayProfiles.AsNoTracking().Where(p => p.EmployeeId == employee)
            .OrderBy(p => p.EffectiveFrom).ThenBy(p => p.IsActive).ToListAsync();
    }

    private static async Task<(int Profiles, int Requests, int Adjustments, int Holds, int Alerts, int Histories)> CountsAsync(H h, Guid employee)
    {
        await using var db = h.Db();
        return (
            await db.EmployeePayProfiles.CountAsync(p => p.EmployeeId == employee),
            await db.PayProfileChangeRequests.CountAsync(r => r.EmployeeId == employee),
            await db.PayrollAdjustments.CountAsync(a => a.EmployeeId == employee),
            await db.PaymentHolds.CountAsync(x => x.EmployeeId == employee),
            await db.FraudAlerts.CountAsync(a => a.EmployeeId == employee),
            await db.EmployeePayProfileHistories.CountAsync(x => x.EmployeeId == employee));
    }

    [Fact]
    public async Task A_new_employees_first_profile_is_inactive_unpriced_and_dated_today_in_the_payroll_calendar()
    {
        // 30 November 2026, 22:30 UTC: already 1 December in Damascus.
        var h = await new H(new DateTimeOffset(2026, 11, 30, 22, 30, 0, TimeSpan.Zero)).SeedAsync();

        await CreatedAsync(h, NewHire, Unit, "law50");

        var profile = Assert.Single(await ProfilesAsync(h, NewHire));
        Assert.Equal((false, 0m, (Guid?)Unit, "law50"), (profile.IsActive, profile.BaseSalary, profile.OrgUnitId, profile.RegimeCode));
        Assert.Equal(Utc(2026, 12, 1), profile.EffectiveFrom);          // a calendar date, never DateTime.UtcNow
        Assert.Null(profile.EffectiveTo);
        Assert.Null(profile.PayGradeId);
    }

    [Fact]
    public async Task Employee_created_then_contract_activated_leaves_an_active_profile_with_the_unit_and_the_regime()
    {
        var h = await new H().SeedAsync();
        await CreatedAsync(h, NewHire, Unit, "law50");
        var contract = Guid.NewGuid();

        await ActivateAsync(h, NewHire, Utc(2026, 12, 1), 5000m, "G5", contract);

        var profiles = await ProfilesAsync(h, NewHire);
        var active = Assert.Single(profiles, p => p.IsActive);
        Assert.Equal((Unit, "law50", 5000m, h.GradeId), (active.OrgUnitId, active.RegimeCode, active.BaseSalary, active.PayGradeId));
        Assert.Equal(Utc(2026, 12, 1), active.EffectiveFrom);
        // The placeholder is closed the day before, and stays in the record.
        var placeholder = Assert.Single(profiles, p => !p.IsActive);
        Assert.Equal(Utc(2026, 11, 30), placeholder.EffectiveTo);

        // The activation is an Applied request row of the automatic source, with the contract's id; nobody decided it.
        await using var db = h.Db();
        var request = await db.PayProfileChangeRequests.AsNoTracking().SingleAsync(r => r.EmployeeId == NewHire);
        Assert.Equal(
            (PayProfileChangeState.Applied, PayProfileChangeSource.ContractActivated, contract.ToString("D"), PayProfileChangeKind.BaseSalaryAndGrade),
            (request.State, request.Source, request.SourceReference, request.ChangeKind));
        Assert.Equal((active.Id, Guid.Empty, (Guid?)null, (Guid?)Unit), (request.AppliedProfileId!.Value, request.RequestedBy, request.DecidedBy, request.OrgUnitId));
        Assert.Equal((5000m, h.GradeId, new DateOnly(2026, 12, 1)), (request.ProposedBaseSalary!.Value, request.ProposedPayGradeId!.Value, request.EffectiveFrom));
        Assert.Null(request.CurrentProfileId);                           // no profile was in force
        // One event, of the application, and PAY-029's zero adjustment.
        var applied = Assert.IsType<PayProfileChangeAppliedEvent>(Assert.Single(h.Events.Published));
        Assert.Equal((request.Id, active.Id, placeholder.Id, "ContractActivated", contract.ToString("D"), 5000m),
            (applied.RequestId, applied.NewProfileId, applied.PreviousProfileId!.Value, applied.Source, applied.SourceReference, applied.BaseSalary));
        Assert.Null(applied.PreviousBaseSalary);
        var adjustment = await db.PayrollAdjustments.AsNoTracking().SingleAsync(a => a.EmployeeId == NewHire);
        Assert.Equal(0m, adjustment.Amount);
        Assert.Contains(contract.ToString(), adjustment.AdjustmentReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.PaymentHolds.AnyAsync(x => x.EmployeeId == NewHire));
    }

    [Fact]
    public async Task Contract_activated_then_employee_created_fills_the_empty_unit_and_regime_of_the_active_profile()
    {
        var h = await new H().SeedAsync();

        await ActivateAsync(h, NewHire, Utc(2026, 12, 1), 5000m, "G5");
        var before = Assert.Single(await ProfilesAsync(h, NewHire));
        Assert.Equal((true, (Guid?)null, (string?)null), (before.IsActive, before.OrgUnitId, before.RegimeCode));   // nothing was known yet

        await CreatedAsync(h, NewHire, Unit, " law50 ");

        var after = Assert.Single(await ProfilesAsync(h, NewHire));     // no second profile
        Assert.Equal(before.Id, after.Id);
        Assert.Equal((true, (Guid?)Unit, "law50", 5000m, h.GradeId), (after.IsActive, after.OrgUnitId, after.RegimeCode, after.BaseSalary, after.PayGradeId));
        Assert.Equal(before.EffectiveFrom, after.EffectiveFrom);
    }

    [Fact]
    public async Task A_late_employee_created_never_overwrites_a_value_a_later_event_set()
    {
        var h = await new H().SeedAsync();                // the seeded employee: active, in Unit, regime law50
        await using (var db = h.Db())
        {
            // A transfer moved him since.
            var profile = await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == H.Employee);
            profile.OrgUnitId = OtherUnit;
            profile.RegimeCode = "contract";
            await db.SaveChangesAsync();
        }

        await CreatedAsync(h, H.Employee, Unit, "law50");
        await CreatedAsync(h, H.Employee, null, null);

        var kept = Assert.Single(await ProfilesAsync(h, H.Employee));
        Assert.Equal((OtherUnit, "contract"), (kept.OrgUnitId, kept.RegimeCode));
    }

    [Fact]
    public async Task A_late_employee_created_fills_only_what_is_empty()
    {
        var h = await new H().SeedAsync(withUnit: false);       // active, regime law50, no unit

        await CreatedAsync(h, H.Employee, Unit, "contract");

        var profile = Assert.Single(await ProfilesAsync(h, H.Employee));
        Assert.Equal((Unit, "law50"), (profile.OrgUnitId, profile.RegimeCode));      // the unit was empty; the regime was not
    }

    [Fact]
    public async Task An_activation_written_while_the_employees_record_was_being_handled_gets_the_unit_and_the_regime()
    {
        // Both handlers read no profile; the activation's version (no unit, no regime) is saved while this event writes the
        // placeholder. The event reads again after its save and fills what that version lacks.
        var h = await new H().SeedAsync();
        await using (var db = h.Db())
        {
            var once = false;
            db.SavedChanges += (_, _) =>
            {
                if (once) return;
                once = true;
                using var other = h.Db();
                other.Add(new EmployeePayProfile { EmployeeId = NewHire, IsActive = true, BaseSalary = 5000m, EffectiveFrom = Utc(2026, 12, 1) });
                other.SaveChanges();
            };
            await new HandleEmployeeCreatedHandler(new PayrollRepository(db), h.Calendar)
                .Handle(new HandleEmployeeCreatedCommand(NewHire, Unit, "law50"), CancellationToken.None);
        }

        var profiles = await ProfilesAsync(h, NewHire);
        var active = Assert.Single(profiles, p => p.IsActive);
        Assert.Equal(((Guid?)Unit, "law50", 5000m), (active.OrgUnitId, active.RegimeCode, active.BaseSalary));
        Assert.Single(profiles, p => !p.IsActive);                       // the placeholder stays, as the record of the event
    }

    [Fact]
    public async Task An_activation_over_the_profile_in_force_keeps_the_unit_and_the_regime_and_counts_no_day_twice()
    {
        var h = await new H().SeedAsync();                // active from 1 January 2026, Unit, law50, G5, 4500

        await ActivateAsync(h, H.Employee, Utc(2026, 12, 1), 5000m, "G6");

        var profiles = await ProfilesAsync(h, H.Employee);
        var (previous, next) = (profiles.Single(p => !p.IsActive), profiles.Single(p => p.IsActive));
        Assert.Equal((Unit, "law50", 5000m, h.HigherGradeId), (next.OrgUnitId, next.RegimeCode, next.BaseSalary, next.PayGradeId));
        // The previous version ends on 30 November: 1 December belongs to the new version alone (it used to end ON the start date).
        Assert.Equal(Utc(2026, 11, 30), previous.EffectiveTo);
        Assert.Equal(Utc(2026, 12, 1), next.EffectiveFrom);
        await using var db = h.Db();
        var history = await db.EmployeePayProfileHistories.AsNoTracking().SingleAsync(x => x.EmployeeId == H.Employee);
        Assert.Equal((previous.Id, $"تفعيل عقد {ContractType}"), (history.SourceProfileId, history.ChangeReason));
        var request = await db.PayProfileChangeRequests.AsNoTracking().SingleAsync(r => r.EmployeeId == H.Employee);
        Assert.Equal((previous.Id, 4500m, (Guid?)Unit), (request.CurrentProfileId!.Value, request.CurrentBaseSalary!.Value, request.OrgUnitId));
    }

    [Fact]
    public async Task An_activation_with_no_salary_over_a_profile_in_force_keeps_its_salary_and_grade_as_before()
    {
        var h = await new H().SeedAsync();

        await ActivateAsync(h, H.Employee, Utc(2026, 12, 1), salary: null, grade: null, requireSalary: true);

        var next = (await ProfilesAsync(h, H.Employee)).Single(p => p.IsActive);
        // PAY-029 as it was: the salary and the grade of the profile in force, an active version, and the review hold.
        Assert.Equal((4500m, h.GradeId, (Guid?)Unit, "law50"), (next.BaseSalary, next.PayGradeId!.Value, next.OrgUnitId, next.RegimeCode));
        await using var db = h.Db();
        Assert.StartsWith(HandleContractActivatedHandler.RuleNoSalary, (await db.PaymentHolds.AsNoTracking().SingleAsync(x => x.EmployeeId == H.Employee)).Reason);
        var request = await db.PayProfileChangeRequests.AsNoTracking().SingleAsync(r => r.EmployeeId == H.Employee);
        Assert.Equal((PayProfileChangeState.Applied, (decimal?)null, (Guid?)null), (request.State, request.ProposedBaseSalary, request.ProposedPayGradeId));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task A_first_activation_with_no_salary_makes_the_unpriced_profile_pay029_makes_with_the_unit_and_the_regime(
        bool requireSalary, bool expectedActive)
    {
        var h = await new H().SeedAsync();
        await CreatedAsync(h, NewHire, Unit, "law50");

        await ActivateAsync(h, NewHire, Utc(2026, 12, 1), salary: null, grade: "UNKNOWN", requireSalary: requireSalary);

        var profiles = await ProfilesAsync(h, NewHire);
        var made = profiles.Single(p => p.EffectiveFrom == Utc(2026, 12, 1));
        Assert.Equal((expectedActive, 0m, (Guid?)Unit, "law50"), (made.IsActive, made.BaseSalary, made.OrgUnitId, made.RegimeCode));
        Assert.Equal(expectedActive ? 1 : 0, profiles.Count(p => p.IsActive));
        await using var db = h.Db();
        var hold = await db.PaymentHolds.AsNoTracking().SingleAsync(x => x.EmployeeId == NewHire);
        Assert.StartsWith(HandleContractActivatedHandler.RuleNoSalary, hold.Reason);
        Assert.Equal((PaymentHoldType.Manual, PaymentHoldStatus.Active, (Guid?)null), (hold.HoldType, hold.Status, hold.PayrollRunId));
        // Recorded as applied all the same: the activation happened, and its (unpriced) version exists.
        var request = await db.PayProfileChangeRequests.AsNoTracking().SingleAsync(r => r.EmployeeId == NewHire);
        Assert.Equal((PayProfileChangeState.Applied, made.Id), (request.State, request.AppliedProfileId!.Value));
    }

    [Fact]
    public async Task A_second_activation_prices_the_profile_the_first_left_unpriced_and_inactive()
    {
        var h = await new H().SeedAsync();
        await CreatedAsync(h, NewHire, Unit, "law50");
        await ActivateAsync(h, NewHire, Utc(2026, 12, 1), salary: null, grade: null, requireSalary: true);

        await ActivateAsync(h, NewHire, Utc(2026, 12, 1), 5000m, "G5", requireSalary: true);

        var active = Assert.Single(await ProfilesAsync(h, NewHire), p => p.IsActive);
        Assert.Equal((5000m, (Guid?)Unit, "law50", Utc(2026, 12, 1)), (active.BaseSalary, active.OrgUnitId, active.RegimeCode, active.EffectiveFrom));
    }

    [Fact]
    public async Task A_salary_outside_the_grade_still_makes_the_version_and_raises_its_review_hold()
    {
        var h = await new H().SeedAsync();
        await CreatedAsync(h, NewHire, Unit, "law50");

        await ActivateAsync(h, NewHire, Utc(2026, 12, 1), 7000m, "G5");          // G5: 4000 to 6000

        var active = Assert.Single(await ProfilesAsync(h, NewHire), p => p.IsActive);
        Assert.Equal((7000m, (Guid?)Unit), (active.BaseSalary, active.OrgUnitId));    // an automatic source skips the grade's bounds
        await using var db = h.Db();
        var hold = await db.PaymentHolds.AsNoTracking().SingleAsync(x => x.EmployeeId == NewHire);
        Assert.StartsWith(HandleContractActivatedHandler.RuleOutOfRange, hold.Reason);
        var alert = await db.FraudAlerts.AsNoTracking().SingleAsync(a => a.EmployeeId == NewHire);
        Assert.Equal((HandleContractActivatedHandler.RuleOutOfRange, hold.Id), (alert.RuleId, alert.PaymentHoldId!.Value));
    }

    [Fact]
    public async Task A_message_delivered_twice_changes_nothing_the_second_time()
    {
        var h = await new H().SeedAsync();
        var contract = Guid.NewGuid();
        await ActivateAsync(h, H.Employee, Utc(2026, 12, 1), 7000m, "G5", contract);      // with a review hold
        var once = await CountsAsync(h, H.Employee);
        var profiles = (await ProfilesAsync(h, H.Employee)).Select(p => (p.Id, p.IsActive, p.EffectiveTo, p.BaseSalary)).ToList();
        var events = h.Events.Published.Count;

        await ActivateAsync(h, H.Employee, Utc(2026, 12, 1), 7000m, "G5", contract);
        await ActivateAsync(h, H.Employee, Utc(2026, 12, 1), 7000m, "G5", contract);

        Assert.Equal(once, await CountsAsync(h, H.Employee));
        Assert.Equal((2, 1, 1, 1, 1, 1), once);
        Assert.Equal(profiles, (await ProfilesAsync(h, H.Employee)).Select(p => (p.Id, p.IsActive, p.EffectiveTo, p.BaseSalary)).ToList());
        Assert.Equal(events, h.Events.Published.Count);
    }

    [Fact]
    public async Task An_activation_dated_before_the_version_in_force_is_consumed_with_a_review_hold_and_changes_nothing_else()
    {
        var h = await new H().SeedAsync();                // in force from 1 January 2026
        var before = (await ProfilesAsync(h, H.Employee)).Single();
        var contract = Guid.NewGuid();

        await ActivateAsync(h, H.Employee, Utc(2025, 6, 1), 5000m, "G5", contract);       // no exception: the message is consumed

        // Nothing but the hold and its alert: no version, no request row, no adjustment, no history, no event.
        Assert.Equal((1, 0, 0, 1, 1, 0), await CountsAsync(h, H.Employee));
        var after = (await ProfilesAsync(h, H.Employee)).Single();
        Assert.Equal((before.Id, true, (DateTime?)null, 4500m), (after.Id, after.IsActive, after.EffectiveTo, after.BaseSalary));
        Assert.Empty(h.Events.Published);
        await using (var db = h.Db())
        {
            var hold = await db.PaymentHolds.AsNoTracking().SingleAsync(x => x.EmployeeId == H.Employee);
            Assert.StartsWith(HandleContractActivatedHandler.RuleActivationRefused, hold.Reason);
            Assert.StartsWith("PAY055-ACTIVATION", hold.Reason);
            Assert.Equal((PaymentHoldType.Manual, PaymentHoldStatus.Active, (Guid?)null), (hold.HoldType, hold.Status, hold.PayrollRunId));
            var alert = await db.FraudAlerts.AsNoTracking().SingleAsync(a => a.EmployeeId == H.Employee);
            Assert.Equal((HandleContractActivatedHandler.RuleActivationRefused, hold.Id, hold.FraudAlertId), (alert.RuleId, alert.PaymentHoldId!.Value, (Guid?)alert.Id));
            Assert.Contains(PayProfileChangeMessages.BeforeCurrentVersion, alert.Details, StringComparison.Ordinal);
            Assert.Contains(contract.ToString(), alert.Details, StringComparison.OrdinalIgnoreCase);
        }

        // Delivered again, it does not loop and places no second hold.
        await ActivateAsync(h, H.Employee, Utc(2025, 6, 1), 5000m, "G5", contract);
        Assert.Equal((1, 0, 0, 1, 1, 0), await CountsAsync(h, H.Employee));

        // Another contract refused while the first still waits is recorded too: one hold per refused contract, each naming its own.
        var other = Guid.NewGuid();
        await ActivateAsync(h, H.Employee, Utc(2025, 7, 1), 5000m, "G5", other);
        await ActivateAsync(h, H.Employee, Utc(2025, 7, 1), 5000m, "G5", other);
        Assert.Equal((1, 0, 0, 2, 2, 0), await CountsAsync(h, H.Employee));
        await using (var db = h.Db())
        {
            var reasons = await db.PaymentHolds.AsNoTracking().Where(x => x.EmployeeId == H.Employee).Select(x => x.Reason).ToListAsync();
            Assert.Single(reasons, r => r.Contains(contract.ToString("D"), StringComparison.OrdinalIgnoreCase));
            Assert.Single(reasons, r => r.Contains(other.ToString("D"), StringComparison.OrdinalIgnoreCase));
            Assert.All(reasons, r => Assert.StartsWith(HandleContractActivatedHandler.RuleActivationRefused + ":", r));
        }
    }

    [Fact]
    public async Task An_activation_dated_inside_a_closed_version_is_consumed_with_a_review_hold()
    {
        var h = await new H().SeedAsync();
        await using (var db = h.Db())
        {
            // A re-hire: the employee's only profile was closed on 30 June 2026.
            var profile = await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == H.Employee);
            profile.IsActive = false;
            profile.EffectiveTo = Utc(2026, 6, 30);
            await db.SaveChangesAsync();
        }

        await ActivateAsync(h, H.Employee, Utc(2026, 6, 15), 5000m, "G5");

        Assert.Equal((1, 0, 0, 1, 1, 0), await CountsAsync(h, H.Employee));
        await using (var db = h.Db())
        {
            Assert.Contains(PayProfileChangeMessages.InsidePreviousVersion,
                (await db.FraudAlerts.AsNoTracking().SingleAsync(a => a.EmployeeId == H.Employee)).Details, StringComparison.Ordinal);
        }

        // The day after the closed version ends, the new contract is applied, with the employee's unit and regime and its own salary.
        await ActivateAsync(h, H.Employee, Utc(2026, 7, 1), 5000m, "G5");
        var profiles = await ProfilesAsync(h, H.Employee);
        var active = profiles.Single(p => p.IsActive);
        Assert.Equal((Unit, "law50", 5000m, Utc(2026, 7, 1)), (active.OrgUnitId, active.RegimeCode, active.BaseSalary, active.EffectiveFrom));
        Assert.Equal(Utc(2026, 6, 30), profiles.Single(p => !p.IsActive).EffectiveTo);     // the closed version kept its end
    }

    /// <summary>An applier that answers as a concurrent save would: nothing saved, a conflict.</summary>
    private sealed class ConflictingApplier : IPayProfileChangeApplier
    {
        public Task<PayProfileChangeResult?> CheckAsync(PayProfileChangeRequest request, CancellationToken ct = default) =>
            Task.FromResult<PayProfileChangeResult?>(null);

        public Task<PayProfileChangeResult> ApplyAsync(PayProfileChangeRequest request, PayProfileChangeApproval? approval, CancellationToken ct = default) =>
            Task.FromResult(Conflict());

        public Task<PayProfileChangeResult> ApplySystemChangeAsync(
            Guid employeeId, PayProfileChangeKind kind, PayProfileChangeSource source, string? sourceReference, decimal? proposedBaseSalary,
            Guid? proposedPayGradeId, DateOnly effectiveFrom, CancellationToken ct = default) => Task.FromResult(Conflict());

        public Task<PayProfileChangeResult> ApplySystemChangeAsync(
            Guid employeeId, PayProfileChangeKind kind, PayProfileChangeSource source, string? sourceReference, decimal? proposedBaseSalary,
            Guid? proposedPayGradeId, DateOnly effectiveFrom, PayProfileContractActivation? activation, CancellationToken ct = default) =>
            Task.FromResult(Conflict());

        private static PayProfileChangeResult Conflict() =>
            PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.DecidedConcurrently);
    }

    [Fact]
    public async Task A_concurrent_save_is_thrown_so_the_message_is_delivered_again_and_nothing_of_it_is_kept()
    {
        var h = await new H().SeedAsync();

        var thrown = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            ActivateAsync(h, H.Employee, Utc(2026, 12, 1), 7000m, "G5", applier: new ConflictingApplier()));
        // The bus retries a concurrency failure, and sends any other InvalidOperationException to the error queue without a retry.
        Assert.True(HRManagement.ServiceDefaults.Messaging.MessagingFaults.IsTransient(thrown));
        Assert.False(HRManagement.ServiceDefaults.Messaging.MessagingFaults.IsTransient(new InvalidOperationException("x")));

        // Not the adjustment and not the PAY-029 hold the handler had staged.
        Assert.Equal((1, 0, 0, 0, 0, 0), await CountsAsync(h, H.Employee));
    }

    [Fact]
    public async Task An_open_request_of_the_employee_is_superseded_by_the_activation()
    {
        var h = await new H().SeedAsync();
        var submitted = await h.SubmitAsync(5000m, effectiveFrom: new DateOnly(2026, 12, 1));
        Assert.True(submitted.Success, submitted.Error);

        await ActivateAsync(h, H.Employee, Utc(2026, 12, 1), 5200m, "G5");

        await using var db = h.Db();
        var requests = await db.PayProfileChangeRequests.AsNoTracking().Where(r => r.EmployeeId == H.Employee).ToListAsync();
        var activation = requests.Single(r => r.Source == PayProfileChangeSource.ContractActivated);
        var human = requests.Single(r => r.Id == submitted.RequestId);
        Assert.Equal((PayProfileChangeState.Superseded, activation.Id), (human.State, human.SupersededByRequestId!.Value));
    }

    // --- the applier ---

    [Fact]
    public async Task The_activations_terms_are_refused_for_any_other_source()
    {
        var h = await new H().SeedAsync();
        await using var db = h.Db();

        var result = await h.Applier(db).ApplySystemChangeAsync(
            H.Employee, PayProfileChangeKind.BaseSalary, PayProfileChangeSource.Promotion, "قرار 9", 5000m, null, new DateOnly(2026, 12, 1),
            new PayProfileContractActivation(true, "ترفيع"));

        Assert.Equal((false, PayProfileChangeFailure.Invalid, PayProfileChangeMessages.ActivationTermsForContractOnly), (result.Success, result.Failure, result.Error));
        Assert.Empty(await db.PayProfileChangeRequests.ToListAsync());
    }

    [Fact]
    public async Task Without_the_activations_terms_an_automatic_change_still_needs_a_profile_in_force_and_a_proposal()
    {
        var h = await new H().SeedAsync();
        await CreatedAsync(h, NewHire, Unit, "law50");            // a placeholder only
        await using var db = h.Db();

        var noProfile = await h.Applier(db).ApplySystemChangeAsync(
            NewHire, PayProfileChangeKind.BaseSalary, PayProfileChangeSource.ContractActivated, "c", 5000m, null, new DateOnly(2026, 12, 1));
        var nothing = await h.Applier(db).ApplySystemChangeAsync(
            H.Employee, PayProfileChangeKind.BaseSalary, PayProfileChangeSource.Promotion, "c", null, null, new DateOnly(2026, 12, 1));

        Assert.Equal(PayProfileChangeMessages.NoActiveProfile, noProfile.Error);
        Assert.Equal(PayProfileChangeMessages.NothingProposed, nothing.Error);
        Assert.Single(await ProfilesAsync(h, NewHire));
    }

    [Fact]
    public async Task A_handler_built_from_a_repository_alone_applies_the_activation_and_stages_no_event()
    {
        // The two-argument construction PAY-029's own tests use: an applier with no bus. The host always injects the registered one.
        var h = await new H().SeedAsync();
        await using (var db = h.Db())
        {
            await new HandleContractActivatedHandler(new PayrollRepository(db))
                .Handle(new HandleContractActivatedCommand(Guid.NewGuid(), H.Employee, ContractType, Utc(2026, 12, 1), 5000m, "G5"), CancellationToken.None);
        }

        var active = (await ProfilesAsync(h, H.Employee)).Single(p => p.IsActive);
        Assert.Equal((5000m, (Guid?)Unit, "law50"), (active.BaseSalary, active.OrgUnitId, active.RegimeCode));
        Assert.Equal((2, 1, 1, 0, 0, 1), await CountsAsync(h, H.Employee));
        Assert.Empty(h.Events.Published);
    }
}
