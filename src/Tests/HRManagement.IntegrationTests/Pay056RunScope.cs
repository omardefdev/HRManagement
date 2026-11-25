using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-56: a payroll service on an in-memory SQLite database, as the handlers run it, for the run's life: a published framework
/// version, employees with pay profiles, inputs, and the acts of a cycle through the mediator. One scope is one request's services.
/// </summary>
internal sealed class Pay056RunScope : IAsyncDisposable
{
    public static readonly Guid Processor = Guid.Parse("56000000-0000-4000-8000-0000000000c1");
    public static readonly Guid Approver = Guid.Parse("56000000-0000-4000-8000-0000000000c2");
    public static readonly Guid Poster = Guid.Parse("56000000-0000-4000-8000-0000000000c3");
    public static readonly Guid Reverser = Guid.Parse("56000000-0000-4000-8000-0000000000c4");
    public static readonly Guid OtherApprover = Guid.Parse("56000000-0000-4000-8000-0000000000c5");

    private readonly ServiceProvider _root;
    private readonly AsyncServiceScope _scope;

    private Pay056RunScope(ServiceProvider root, AsyncServiceScope scope)
    {
        _root = root;
        _scope = scope;
    }

    public PayrollDbContext Db => _scope.ServiceProvider.GetRequiredService<PayrollDbContext>();

    public IMediator Mediator => _scope.ServiceProvider.GetRequiredService<IMediator>();

    public IServiceProvider Services => _scope.ServiceProvider;

    public static async Task<Pay056RunScope> CreateAsync(Action<IServiceCollection>? configure = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PayrollDbContext>(o => o.UseSqlite(connection));
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
        configure?.Invoke(services);

        var root = services.BuildServiceProvider();
        var scope = root.CreateAsyncScope();
        var harness = new Pay056RunScope(root, scope);
        await harness.Db.Database.EnsureCreatedAsync();

        var framework = PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
        framework.ContentHash = PayrollConfigurationHashService.ComputeContentHash(framework);
        framework.EffectiveFrom = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        harness.Db.PayrollConfigurations.Add(framework);
        await harness.Db.SaveChangesAsync();
        return harness;
    }

    /// <summary>A fresh request against the same database: new services, a new context.</summary>
    public AsyncServiceScope NewRequest() => _root.CreateAsyncScope();

    public async Task<Guid> EmployeeAsync(decimal baseSalary = 900_000m)
    {
        var employee = Guid.NewGuid();
        Db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(employee, baseSalary));
        await Db.SaveChangesAsync();
        return employee;
    }

    public async Task<PayrollAdjustment> AdjustmentAsync(Guid employee, decimal amount, string reason = "تسوية يدوية")
    {
        var adjustment = new PayrollAdjustment { EmployeeId = employee, Amount = amount, AdjustmentReason = reason };
        Db.PayrollAdjustments.Add(adjustment);
        await Db.SaveChangesAsync();
        return adjustment;
    }

    public async Task<DeductionRecord> DeductionAsync(Guid employee, decimal amount, string type = "Manual", DateOnly? dueDate = null)
    {
        var deduction = new DeductionRecord { EmployeeId = employee, Amount = amount, DeductionType = type, DueDate = dueDate };
        Db.DeductionRecords.Add(deduction);
        await Db.SaveChangesAsync();
        return deduction;
    }

    public async Task<AbsenceDeduction> AbsenceAsync(Guid employee, DateOnly periodStart, DateOnly periodEnd, decimal amount)
    {
        var absence = new AbsenceDeduction
        {
            EmployeeId = employee,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            AbsentDays = 1,
            Amount = amount,
            Reason = "خصم غياب",
        };
        Db.AbsenceDeductions.Add(absence);
        await Db.SaveChangesAsync();
        return absence;
    }

    public async Task<Guid> CreateRunAsync(int year = 2028, int month = 1)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var (created, id, error) = await Mediator.Send(
            new CreatePayrollRunCommand(start, start.AddMonths(1).AddDays(-1), start.AddMonths(1)));
        Assert.True(created, error);
        return id!.Value;
    }

    public async Task<(bool Ok, int Lines, string? Error)> CalculateAsync(Guid runId)
    {
        Db.ChangeTracker.Clear();
        return await Mediator.Send(new CalculatePayrollRunCommand(runId));
    }

    /// <summary>Create, calculate (which validates and freezes), and return the run's id.</summary>
    public async Task<Guid> CalculatedRunAsync(int year = 2028, int month = 1)
    {
        var runId = await CreateRunAsync(year, month);
        var (ok, _, error) = await CalculateAsync(runId);
        Assert.True(ok, error);
        return runId;
    }

    public async Task SubmitAsync(Guid runId)
    {
        var (ok, error) = await Mediator.Send(new SubmitPayrollRunForReviewCommand(runId));
        Assert.True(ok, error);
    }

    public async Task ApproveAsync(Guid runId, Guid? approver = null)
    {
        var (ok, error) = await Mediator.Send(new ApprovePayrollRunCommand(runId, approver ?? Approver, "payroll-approver", null));
        Assert.True(ok, error);
    }

    /// <summary>The closed audit record a posting needs: a test-only precondition (no persona closes one before PLAN-58, G-PAY-05).</summary>
    public async Task CloseAuditAsync(Guid runId)
    {
        Db.PayrollAuditRecords.Add(new PayrollAuditRecord
        {
            PayrollRunId = runId,
            ClosedBy = Guid.NewGuid(),
            ClosedAt = DateTime.UtcNow,
            Status = AuditStatus.Closed,
            Findings = "لا ملاحظات",
        });
        await Db.SaveChangesAsync();
    }

    public async Task<Guid> ApprovedRunAsync(int year = 2028, int month = 1)
    {
        var runId = await CalculatedRunAsync(year, month);
        await SubmitAsync(runId);
        await ApproveAsync(runId);
        await CloseAuditAsync(runId);
        return runId;
    }

    public Task<(bool Success, string? Error)> PostAsync(Guid runId, Guid? poster = null)
    {
        Db.ChangeTracker.Clear();
        return Mediator.Send(new PostPayrollRunCommand(runId, poster ?? Poster));
    }

    public async Task<Guid> PostedRunAsync(int year = 2028, int month = 1)
    {
        var runId = await ApprovedRunAsync(year, month);
        var (ok, error) = await PostAsync(runId);
        Assert.True(ok, error);
        return runId;
    }

    public async Task<PayrollRun> RunAsync(Guid runId)
    {
        Db.ChangeTracker.Clear();
        return await Db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
    }

    public async Task<List<SalaryLine>> LinesAsync(Guid runId)
    {
        Db.ChangeTracker.Clear();
        return await Db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == runId).OrderBy(l => l.EmployeeId).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
    }
}
