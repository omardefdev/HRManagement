using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.InboundEvents;
using Payroll.Application.Options;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Repositories;

namespace HRManagement.UnitTests;

/// <summary>PAY-029 (H): the pay profile takes the offer's grade and salary; deviations raise a review hold.</summary>
public class Pay029PayProfileTests
{
    private static readonly DateTime Start = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private static PayrollDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PayrollDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>An expired 2025 scale and the 2026 scale in force, both with grade G5.</summary>
    private static async Task<(PayGrade Old, PayGrade Current)> SeedScalesAsync(PayrollDbContext db)
    {
        var old = new SalaryScale { Code = "S-2025", NameAr = "سلم 2025", EffectiveFrom = new DateTime(2025, 1, 1), EffectiveTo = new DateTime(2025, 12, 31), IsActive = true };
        var current = new SalaryScale { Code = "S-2026", NameAr = "سلم 2026", EffectiveFrom = new DateTime(2026, 1, 1), IsActive = true };
        var inactive = new SalaryScale { Code = "S-DRAFT", NameAr = "مسودة", EffectiveFrom = new DateTime(2026, 6, 1), IsActive = false };
        var oldGrade = new PayGrade { SalaryScale = old, GradeCode = "G5", GradeNameAr = "الخامسة", BaseSalary = 3000, MinSalary = 2500, MaxSalary = 4000 };
        var currentGrade = new PayGrade { SalaryScale = current, GradeCode = "G5", GradeNameAr = "الخامسة", BaseSalary = 4500, MinSalary = 4000, MaxSalary = 6000 };
        var draftGrade = new PayGrade { SalaryScale = inactive, GradeCode = "G5", GradeNameAr = "الخامسة", BaseSalary = 9000, MinSalary = 9000, MaxSalary = 9900 };
        db.AddRange(old, current, inactive, oldGrade, currentGrade, draftGrade);
        await db.SaveChangesAsync();
        return (oldGrade, currentGrade);
    }

    private static Task HandleAsync(PayrollDbContext db, decimal? salary, string? grade, bool requireSalary = false, Guid? employeeId = null) =>
        new HandleContractActivatedHandler(
                new PayrollRepository(db),
                Microsoft.Extensions.Options.Options.Create(new PayrollActivationOptions { RequireBaseSalaryOnActivation = requireSalary }))
            .Handle(new HandleContractActivatedCommand(Guid.NewGuid(), employeeId ?? EmployeeId, "ACC-LAW50-PERM", Start, salary, grade), CancellationToken.None);

    private static readonly Guid EmployeeId = Guid.Parse("29290000-0000-4000-8000-0000000000e1");

    [Fact]
    public async Task Grade_is_resolved_in_the_scale_in_force_at_the_start_date()
    {
        await using var db = CreateDb();
        var (_, current) = await SeedScalesAsync(db);

        await HandleAsync(db, salary: null, grade: "G5");

        var profile = await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == EmployeeId);
        Assert.Equal(current.Id, profile.PayGradeId);
        Assert.Equal(4500m, profile.BaseSalary);
        Assert.True(profile.IsActive);
        Assert.False(await db.PaymentHolds.AnyAsync());
    }

    [Fact]
    public async Task Salary_comes_from_the_event()
    {
        await using var db = CreateDb();
        var (_, current) = await SeedScalesAsync(db);

        await HandleAsync(db, salary: 5000m, grade: "G5");

        var profile = await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == EmployeeId);
        Assert.Equal(5000m, profile.BaseSalary);
        Assert.Equal(current.Id, profile.PayGradeId);
        Assert.False(await db.FraudAlerts.AnyAsync());
    }

    [Fact]
    public async Task Out_of_range_salary_still_creates_the_profile_and_raises_a_review_hold()
    {
        await using var db = CreateDb();
        await SeedScalesAsync(db);

        await HandleAsync(db, salary: 7000m, grade: "G5");
        await HandleAsync(db, salary: 7000m, grade: "G5"); // redelivery: no second hold

        var profile = await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == EmployeeId && p.IsActive);
        Assert.Equal(7000m, profile.BaseSalary);
        var hold = await db.PaymentHolds.SingleAsync(h => h.EmployeeId == EmployeeId);
        Assert.Equal(PaymentHoldStatus.Active, hold.Status);
        Assert.Equal(PaymentHoldType.Manual, hold.HoldType);
        Assert.Null(hold.PayrollRunId);
        Assert.Contains(HandleContractActivatedHandler.OutOfRangeReasonAr, hold.Reason);
        Assert.Equal("الراتب خارج حدود الدرجة", HandleContractActivatedHandler.OutOfRangeReasonAr);
        var alert = await db.FraudAlerts.SingleAsync(a => a.EmployeeId == EmployeeId);
        Assert.Equal(HandleContractActivatedHandler.RuleOutOfRange, alert.RuleId);
        Assert.Equal(hold.Id, alert.PaymentHoldId);
        Assert.Equal(alert.Id, hold.FraudAlertId);
    }

    [Fact]
    public async Task No_salary_keeps_todays_active_profile_and_raises_a_hold()
    {
        await using var db = CreateDb();

        await HandleAsync(db, salary: null, grade: null);

        var profile = await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == EmployeeId);
        Assert.True(profile.IsActive);
        Assert.Equal(0m, profile.BaseSalary);
        var hold = await db.PaymentHolds.SingleAsync(h => h.EmployeeId == EmployeeId);
        Assert.StartsWith(HandleContractActivatedHandler.RuleNoSalary, hold.Reason);
    }

    [Fact]
    public async Task Require_base_salary_option_creates_an_inactive_profile()
    {
        await using var db = CreateDb();

        await HandleAsync(db, salary: null, grade: "UNKNOWN", requireSalary: true);

        var profile = await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == EmployeeId);
        Assert.False(profile.IsActive);
        Assert.True(await db.PaymentHolds.AnyAsync(h => h.EmployeeId == EmployeeId));
    }

    [Fact]
    public async Task Undated_grade_is_used_when_no_scale_is_in_force()
    {
        await using var db = CreateDb();
        var scale = new SalaryScale { Code = "S-FUT", NameAr = "مستقبلي", EffectiveFrom = new DateTime(2030, 1, 1), IsActive = true };
        var grade = new PayGrade { SalaryScale = scale, GradeCode = "G9", GradeNameAr = "التاسعة", BaseSalary = 8000, MinSalary = 7000, MaxSalary = 9000 };
        db.AddRange(scale, grade);
        await db.SaveChangesAsync();

        await HandleAsync(db, salary: null, grade: "G9");

        Assert.Equal(grade.Id, (await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == EmployeeId)).PayGradeId);
    }

    [Fact]
    public async Task Existing_profile_is_closed_and_replaced()
    {
        await using var db = CreateDb();
        await SeedScalesAsync(db);
        db.EmployeePayProfiles.Add(new EmployeePayProfile { EmployeeId = EmployeeId, BaseSalary = 1000, EffectiveFrom = new DateTime(2025, 1, 1), IsActive = true });
        await db.SaveChangesAsync();

        await HandleAsync(db, salary: 5000m, grade: "G5");

        Assert.Equal(1, await db.EmployeePayProfiles.CountAsync(p => p.EmployeeId == EmployeeId && p.IsActive));
        Assert.Equal(5000m, (await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == EmployeeId && p.IsActive)).BaseSalary);
    }
}
