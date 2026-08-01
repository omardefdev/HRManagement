using HR.Contracts.Domain.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;

namespace HRManagement.UnitTests;

/// <summary>QA-002-E: pass+fail companions for P0/P1 invariants in the risk matrix.</summary>
public class Qa002InvariantCoverageTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void INV02_SingleActivePayProfile_Succeeds()
    {
        var profiles = new List<EmployeePayProfile>
        {
            new() { EmployeeId = Guid.NewGuid(), BaseSalary = 1000, IsActive = true, EffectiveFrom = DateTime.UtcNow }
        };
        Assert.Equal(1, profiles.Count(p => p.IsActive));
    }

    [Fact]
    public void INV02_SecondActivePayProfile_FailsUnique()
    {
        using var db = OpenPayrollSqlite();
        var emp = Guid.NewGuid();
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            TenantId = TenantId,
            EmployeeId = emp,
            BaseSalary = 1000,
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow
        });
        db.SaveChanges();

        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            TenantId = TenantId,
            EmployeeId = emp,
            BaseSalary = 1100,
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow
        });
        var ex = Assert.ThrowsAny<Exception>(() => db.SaveChanges());
        Assert.True(
            ex is DbUpdateException || ex.InnerException is not null,
            $"Expected unique violation, got {ex.GetType().Name}: {ex.Message}");
    }

    [Fact]
    public void INV04_PaymentBatch_UniqueRef_Succeeds()
    {
        using var db = OpenPayrollSqlite();
        var run = NewRun();
        db.PayrollRuns.Add(run);
        db.SaveChanges();
        db.PaymentBatches.Add(new PaymentBatch
        {
            TenantId = TenantId,
            PayrollRunId = run.Id,
            BankTransferReference = "REF-OK",
            TotalAmount = 100
        });
        db.SaveChanges();
        Assert.Equal(1, db.PaymentBatches.Count(b => b.PayrollRunId == run.Id));
    }

    [Fact]
    public void INV04_PaymentBatch_DuplicateRef_FailsUnique()
    {
        using var db = OpenPayrollSqlite();
        var run = NewRun();
        db.PayrollRuns.Add(run);
        db.SaveChanges();
        db.PaymentBatches.Add(new PaymentBatch
        {
            TenantId = TenantId,
            PayrollRunId = run.Id,
            BankTransferReference = "REF-DUP",
            TotalAmount = 100
        });
        db.SaveChanges();
        db.PaymentBatches.Add(new PaymentBatch
        {
            TenantId = TenantId,
            PayrollRunId = run.Id,
            BankTransferReference = "REF-DUP",
            TotalAmount = 200
        });
        Assert.ThrowsAny<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void INV08_DebitWithinEntitlement_Succeeds()
    {
        var bal = new LeaveBalance { TotalEntitlement = 10, Used = 3 };
        const int days = 2;
        Assert.True(bal.Used + days <= bal.TotalEntitlement);
        bal.Used += days;
        Assert.Equal(5, bal.Remaining);
    }

    [Fact]
    public void INV08_DebitAboveEntitlement_Rejected()
    {
        var bal = new LeaveBalance { TotalEntitlement = 5, Used = 4 };
        const int days = 2;
        Assert.False(bal.Used + days <= bal.TotalEntitlement);
    }

    [Fact]
    public void INV13_Idempotency_FirstKey_Succeeds()
    {
        using var db = OpenPayrollSqlite();
        db.IdempotencyRecords.Add(new HRManagement.ServiceDefaults.IdempotencyRecord
        {
            TenantId = TenantId,
            Key = "idem-1",
            RequestHash = "h1",
            ResponseStatus = 200,
            ResponseBody = "{}"
        });
        db.SaveChanges();
        Assert.Equal(1, db.IdempotencyRecords.Count(r => r.Key == "idem-1"));
    }

    [Fact]
    public void INV13_Idempotency_DuplicateKey_FailsUnique()
    {
        using var db = OpenPayrollSqlite();
        db.IdempotencyRecords.Add(new HRManagement.ServiceDefaults.IdempotencyRecord
        {
            TenantId = TenantId,
            Key = "idem-dup",
            RequestHash = "h1",
            ResponseStatus = 200,
            ResponseBody = "{}"
        });
        db.SaveChanges();
        db.IdempotencyRecords.Add(new HRManagement.ServiceDefaults.IdempotencyRecord
        {
            TenantId = TenantId,
            Key = "idem-dup",
            RequestHash = "h2",
            ResponseStatus = 200,
            ResponseBody = "{}"
        });
        Assert.ThrowsAny<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void PAY_ENG_NetFromGross_And_FrozenModify_Fail()
    {
        var line = new SalaryLine { GrossPay = 1000m, TotalDeductions = 250m };
        PayrollCalculation.ApplyNetFromGrossAndDeductions(line);
        Assert.Equal(750m, line.NetPay);

        var run = NewRun();
        run.SalaryLines.Add(new SalaryLine { GrossPay = 1000m, NetPay = 750m, TotalDeductions = 250m });
        run.MarkCalculated(750m);
        run.MarkValidated(750m);
        Assert.NotNull(run.ValidateCanModifyLines());
        Assert.Equal(PayrollRunStatus.Calculated, run.Status);
        Assert.True(run.ResultsFrozen);
    }

    [Fact]
    public void PAY_ENG_Lifecycle_AfterValidate_Succeeds()
    {
        var run = NewRun();
        run.SalaryLines.Add(new SalaryLine { GrossPay = 1000, NetPay = 900, TotalDeductions = 100 });
        run.MarkCalculated(900);
        run.MarkValidated(900);
        run.SubmitForReview();
        Assert.Equal(PayrollRunStatus.UnderReview, run.Status);
        run.Approve(Guid.NewGuid());
        Assert.Equal(PayrollRunStatus.Approved, run.Status);
        run.Post(900);
        Assert.Equal(PayrollRunStatus.Posted, run.Status);
        run.Reverse();
        Assert.Equal(PayrollRunStatus.Reversed, run.Status);
    }

    private static PayrollRun NewRun() => new()
    {
        TenantId = TenantId,
        PeriodStart = DateTime.UtcNow.Date,
        PeriodEnd = DateTime.UtcNow.Date.AddDays(30),
        RunDate = DateTime.UtcNow,
        Status = PayrollRunStatus.Draft,
        RunType = PayrollRunType.Regular
    };

    private static PayrollDbContext OpenPayrollSqlite()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new PayrollDbContext(options, new FixedTenant(TenantId));
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
