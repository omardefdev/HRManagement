using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.BankAccounts;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Repositories;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-55: the bank account's acts on an in-memory database, as the handlers run them: one employee with an active pay profile in a
/// unit, a processor, two approvers, a bank list a test may fill, and a clock. Every send uses a fresh context, as a request does.
/// </summary>
public sealed class Pay055BankAccountHarness
{
    public static readonly Guid Employee = Guid.Parse("55000000-0000-4000-8000-0000000000e1");
    public static readonly Guid OtherEmployee = Guid.Parse("55000000-0000-4000-8000-0000000000e2");
    public static readonly Guid Unit = Guid.Parse("55000000-0000-4000-8000-0000000000a1");
    public static readonly Guid ForeignUnit = Guid.Parse("55000000-0000-4000-8000-0000000000a2");
    public static readonly Guid Processor = Guid.Parse("55000000-0000-4000-8000-0000000000c1");
    public static readonly Guid Approver = Guid.Parse("55000000-0000-4000-8000-0000000000c2");
    public static readonly Guid OtherApprover = Guid.Parse("55000000-0000-4000-8000-0000000000c3");

    public const string BankName = "مصرف الاختبار";
    public const string HolderName = "موظف الاختبار الأول";
    public const string Reference = "كتاب المصرف 2026/118";
    public const string LocalPattern = "^[0-9]{12}$";

    /// <summary>Two test IBANs whose check digits are computed: never a real account.</summary>
    public static readonly string Iban1 = Pay055BankAccountNumberValidatorTests.TestIban("SY", "000100000000000055000001");
    public static readonly string Iban2 = Pay055BankAccountNumberValidatorTests.TestIban("SY", "000100000000000055000002");

    private readonly string _database = Guid.NewGuid().ToString();

    public Iam053Clock Clock { get; } = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));

    public PayrollBankOptions Banks { get; } = new();

    // PLAN-58: a verification re-pins the employee's held orders in its own transaction; the in-memory store has none (the transaction
    // itself is proved on SQLite and PostgreSQL).
    public PayrollDbContext Db() =>
        new(new DbContextOptionsBuilder<PayrollDbContext>().UseInMemoryDatabase(_database)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);

    public IPayrollCalendar Calendar => new PayrollCalendar(Clock);

    public static OrgAccessScope Restricted(params Guid[] units) => OrgAccessScope.Restricted(units.ToHashSet());

    /// <summary>The seed: the employee's active pay profile in <see cref="Unit"/>, and another employee's in <see cref="ForeignUnit"/>.</summary>
    public async Task<Pay055BankAccountHarness> SeedAsync()
    {
        await using var db = Db();
        db.AddRange(Profile(Employee, Unit), Profile(OtherEmployee, ForeignUnit));
        await db.SaveChangesAsync();
        return this;
    }

    public static EmployeePayProfile Profile(Guid employee, Guid? unit, bool active = true) => new()
    {
        EmployeeId = employee,
        BaseSalary = 4500m,
        EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        IsActive = active,
        OrgUnitId = unit,
        RegimeCode = "law50",
    };

    public Pay055BankAccountHarness WithBank(string code = "CBS", string nameAr = "المصرف التجاري السوري", string pattern = LocalPattern)
    {
        Banks.Banks.Add(new PayrollBankDefinition { Code = code, NameAr = nameAr, AccountRegex = pattern });
        return this;
    }

    // --- the commands, each in its own unit of work ---

    public async Task<BankAccountResult> RegisterAsync(
        string? number = null,
        Guid? employee = null,
        Guid? actor = null,
        BankAccountKind kind = BankAccountKind.Iban,
        string? bankCode = null,
        string? bankName = BankName,
        string? holder = HolderName,
        OrgAccessScope? scope = null)
    {
        await using var db = Db();
        var repository = new PayrollRepository(db);
        return await new RequestBankAccountHandler(
                repository, new BankAccountRules(repository), Microsoft.Extensions.Options.Options.Create(Banks), Calendar)
            .Handle(
                new RequestBankAccountCommand(employee ?? Employee, kind, number ?? Iban1, bankCode, bankName, holder, actor ?? Processor, scope),
                CancellationToken.None);
    }

    public async Task<BankAccountResult> VerifyAsync(
        Guid accountId, string? reference = Reference, Guid? actor = null, Guid? employee = null, OrgAccessScope? scope = null)
    {
        await using var db = Db();
        var repository = new PayrollRepository(db);
        return await new VerifyBankAccountHandler(repository, new BankAccountRules(repository), Calendar)
            .Handle(new VerifyBankAccountCommand(employee ?? Employee, accountId, reference, actor ?? Approver, scope), CancellationToken.None);
    }

    public async Task<BankAccountResult> RejectAsync(
        Guid accountId, string? reason = "الرقم لا يطابق كتاب المصرف", Guid? actor = null, Guid? employee = null, OrgAccessScope? scope = null)
    {
        await using var db = Db();
        var repository = new PayrollRepository(db);
        return await new RejectBankAccountHandler(new BankAccountRules(repository), Calendar)
            .Handle(new RejectBankAccountCommand(employee ?? Employee, accountId, reason, actor ?? Approver, scope), CancellationToken.None);
    }

    public async Task<BankAccountResult> RetireAsync(
        Guid accountId, string? reason = "أغلق الموظف حسابه", Guid? actor = null, Guid? employee = null, OrgAccessScope? scope = null)
    {
        await using var db = Db();
        var repository = new PayrollRepository(db);
        return await new RetireBankAccountHandler(new BankAccountRules(repository), Calendar)
            .Handle(new RetireBankAccountCommand(employee ?? Employee, accountId, reason, actor ?? Approver, scope), CancellationToken.None);
    }

    /// <summary>An account in force: registered by the processor and verified by the approver.</summary>
    public async Task<Guid> ActiveAccountAsync(string? number = null, Guid? employee = null)
    {
        var registered = await RegisterAsync(number, employee);
        Assert.True(registered.Success, registered.Error);
        var verified = await VerifyAsync(registered.AccountId!.Value, employee: employee);
        Assert.True(verified.Success, verified.Error);
        return registered.AccountId.Value;
    }

    // --- reads ---

    public async Task<EmployeeBankAccount> AccountAsync(Guid id)
    {
        await using var db = Db();
        return await db.EmployeeBankAccounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    public async Task<List<EmployeeBankAccount>> AccountsAsync(Guid? employee = null)
    {
        await using var db = Db();
        return await db.EmployeeBankAccounts.AsNoTracking()
            .Where(a => a.EmployeeId == (employee ?? Employee)).OrderBy(a => a.CreatedAt).ToListAsync();
    }

    public async Task<List<PaymentHold>> HoldsAsync()
    {
        await using var db = Db();
        return await db.PaymentHolds.AsNoTracking().OrderBy(h => h.HeldAtUtc).ToListAsync();
    }

    /// <summary>A run with one line of the employee that pinned <paramref name="accountId"/>; optionally a payment order of that line.</summary>
    public async Task<(Guid RunId, Guid LineId)> RunLineAsync(
        Guid? accountId,
        PayrollRunStatus status = PayrollRunStatus.Approved,
        PaymentOrderStatus? order = null,
        int month = 10,
        Guid? employee = null)
    {
        await using var db = Db();
        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, month, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, month, 28, 0, 0, 0, DateTimeKind.Utc),
            RunDate = new DateTime(2026, month, 28, 0, 0, 0, DateTimeKind.Utc),
            Status = status,
        };
        var line = new SalaryLine
        {
            PayrollRunId = run.Id,
            EmployeeId = employee ?? Employee,
            GrossPay = 4500m,
            TotalDeductions = 500m,
            NetPay = 4000m,
            BankAccountId = accountId,
        };
        db.AddRange(run, line);
        if (order is PaymentOrderStatus orderStatus)
        {
            db.Add(new PaymentOrder
            {
                IdempotencyKey = $"payroll:{run.Id:N}:{line.Id:N}",
                PayrollRunId = run.Id,
                PaymentBatchId = Guid.NewGuid(),
                SalaryLineId = line.Id,
                EmployeeId = line.EmployeeId,
                Amount = line.NetPay,
                Status = orderStatus,
            });
        }

        await db.SaveChangesAsync();
        return (run.Id, line.Id);
    }

    /// <summary>A run that is being calculated: its job's snapshot pinned <paramref name="accountId"/> and the employee's line is not written yet.</summary>
    public async Task<Guid> RunSnapshotAsync(Guid? accountId, PayrollRunStatus status = PayrollRunStatus.Calculating, int month = 11, Guid? employee = null)
    {
        await using var db = Db();
        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, month, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, month, 28, 0, 0, 0, DateTimeKind.Utc),
            RunDate = new DateTime(2026, month, 28, 0, 0, 0, DateTimeKind.Utc),
            Status = status,
        };
        var job = new PayrollRunJob { PayrollRunId = run.Id, Status = PayrollRunJobStatus.Paused, Phase = PayrollRunJobPhase.Calculate };
        job.InputSnapshots.Add(new PayrollRunInputSnapshot
        {
            EmployeeId = employee ?? Employee,
            ProfileVersion = "v",
            BankAccountId = accountId,
            SortOrder = 0,
        });
        db.AddRange(run, job);
        await db.SaveChangesAsync();
        return run.Id;
    }

    /// <summary>Gives the line's payment order a real batch in <paramref name="status"/>.</summary>
    public async Task SetBatchOfLineAsync(Guid lineId, PaymentBatchStatus status)
    {
        await using var db = Db();
        var order = await db.PaymentOrders.SingleAsync(o => o.SalaryLineId == lineId);
        var batch = new PaymentBatch { PayrollRunId = order.PayrollRunId, TotalAmount = order.Amount, Status = status, BatchKind = PaymentBatchKind.Disbursement };
        db.Add(batch);
        order.PaymentBatchId = batch.Id;
        await db.SaveChangesAsync();
    }
}
