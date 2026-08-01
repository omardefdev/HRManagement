using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>PAY-004: partial disbursement and arrears ledger.</summary>
public class Pay004ArrearsTests
{

    private static Guid RuleSetId { get; } = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static List<DisbursementPriorityRule> DefaultRules() =>
    [
        new()
        {
            RuleSetId = RuleSetId,
            Category = "MinimumWage",
            Priority = 1,
            MinimumAmount = 0,
            LegalSource = "Law 50/2004 Art. 73",
            IsActive = true
        },
        new()
        {
            RuleSetId = RuleSetId,
            Category = "Pension",
            Priority = 2,
            MinimumAmount = 0,
            LegalSource = "Decree 135/2026",
            IsActive = true
        },
        new()
        {
            RuleSetId = RuleSetId,
            Category = "RegularPayroll",
            Priority = 4,
            MinimumAmount = 0,
            LegalSource = "Treasury circular",
            IsActive = true
        }
    ];

    [Fact]
    public void Full_funding_100_percent_no_arrears_IsPartial_false()
    {
        var run = new PayrollRun { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        var decision = new FundingDecision
        {
            Id = Guid.NewGuid(),
            PayrollRunId = run.Id,
            RequestedAmount = 100_000m,
            ApprovedAmount = 100_000m,
            FundingRatio = 1m,
            PriorityRuleSetId = RuleSetId,
            ApprovedBy = Guid.NewGuid()
        };

        var svc = new PartialDisbursementService();
        var result = svc.Allocate(
            run,
            decision,
            [
                new EmployeeDisbursementClaim(emp1, 60_000m, "RegularPayroll"),
                new EmployeeDisbursementClaim(emp2, 40_000m, "RegularPayroll")
            ],
            DefaultRules());

        Assert.False(result.Batch.IsPartial);
        Assert.Equal(100_000m, result.TotalDisbursed);
        Assert.Empty(result.ArrearsEntries);
        Assert.Equal(result.TotalEntitled, result.TotalDisbursed + result.TotalArrears);
    }

    [Fact]
    public void Sixty_percent_funding_creates_arrears_Paid_plus_Arrears_equals_Original()
    {
        var run = new PayrollRun { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        var emp = Guid.NewGuid();
        var decision = new FundingDecision
        {
            Id = Guid.NewGuid(),
            PayrollRunId = run.Id,
            RequestedAmount = 100_000m,
            ApprovedAmount = 60_000m,
            FundingRatio = 0.60m,
            PriorityRuleSetId = RuleSetId,
            ApprovedBy = Guid.NewGuid()
        };

        var svc = new PartialDisbursementService();
        var result = svc.Allocate(
            run,
            decision,
            [new EmployeeDisbursementClaim(emp, 100_000m, "RegularPayroll")],
            DefaultRules());

        Assert.True(result.Batch.IsPartial);
        Assert.Equal(60_000m, result.Batch.DisbursedAmount);
        Assert.Single(result.ArrearsEntries);

        var entry = result.ArrearsEntries[0];
        Assert.Equal(100_000m, entry.OriginalAmount);
        Assert.Equal(60_000m, entry.PaidAmount);
        Assert.Equal(40_000m, entry.ArrearsAmount);
        Assert.Equal(entry.PaidAmount + entry.ArrearsAmount, entry.OriginalAmount);
        Assert.Equal(ArrearsLedgerStatus.PartiallyPaid, entry.Status);
        Assert.Equal("SYP-new", entry.CurrencyEra);
        Assert.Equal(result.TotalEntitled, result.TotalDisbursed + result.TotalArrears);
    }

    [Fact]
    public void Settlement_clears_balance_without_double_pay()
    {
        var emp = Guid.NewGuid();
        var older = new ArrearsLedgerEntry
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp,
            PayrollRunId = Guid.NewGuid(),
            OriginalAmount = 100_000m,
            PaidAmount = 60_000m,
            ArrearsAmount = 40_000m,
            Status = ArrearsLedgerStatus.PartiallyPaid,
            CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var newer = new ArrearsLedgerEntry
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp,
            PayrollRunId = Guid.NewGuid(),
            OriginalAmount = 50_000m,
            PaidAmount = 0m,
            ArrearsAmount = 50_000m,
            Status = ArrearsLedgerStatus.Pending,
            CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var svc = new ArrearsSettlementService();
        var first = svc.Settle(emp, 40_000m, [older, newer], "settle-key-1");

        Assert.False(first.WasIdempotentReplay);
        Assert.Equal(40_000m, first.AmountApplied);
        Assert.Equal(0m, older.ArrearsAmount);
        Assert.Equal(ArrearsLedgerStatus.FullyPaid, older.Status);
        Assert.Equal(50_000m, newer.ArrearsAmount);

        var replay = svc.Settle(emp, 40_000m, [older, newer], "settle-key-1");
        Assert.True(replay.WasIdempotentReplay);
        Assert.Equal(0m, replay.AmountApplied);
        Assert.Equal(0m, older.ArrearsAmount);
        Assert.Equal(50_000m, newer.ArrearsAmount);

        var second = svc.Settle(emp, 50_000m, [older, newer], "settle-key-2");
        Assert.Equal(50_000m, second.AmountApplied);
        Assert.Equal(0m, newer.ArrearsAmount);
        Assert.Equal(ArrearsLedgerStatus.FullyPaid, newer.Status);

        var balance = ArrearsSettlementService.GetBalance(emp, [older, newer]);
        Assert.Equal(0m, balance.ArrearsTotal);
        Assert.Equal(0, balance.OpenEntryCount);
    }

    [Fact]
    public void Priority_rules_ordered_correctly()
    {
        var run = new PayrollRun { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        var minWageEmp = Guid.NewGuid();
        var pensionEmp = Guid.NewGuid();
        var regularEmp = Guid.NewGuid();

        var decision = new FundingDecision
        {
            Id = Guid.NewGuid(),
            PayrollRunId = run.Id,
            RequestedAmount = 300_000m,
            ApprovedAmount = 150_000m,
            FundingRatio = 0.5m,
            PriorityRuleSetId = RuleSetId,
            ApprovedBy = Guid.NewGuid()
        };

        // Intentionally unsorted claim order — Regular first
        var claims = new[]
        {
            new EmployeeDisbursementClaim(regularEmp, 100_000m, "RegularPayroll"),
            new EmployeeDisbursementClaim(pensionEmp, 100_000m, "Pension"),
            new EmployeeDisbursementClaim(minWageEmp, 100_000m, "MinimumWage")
        };

        var svc = new PartialDisbursementService();
        var result = svc.Allocate(run, decision, claims, DefaultRules());

        var byEmp = result.Allocations.ToDictionary(a => a.EmployeeId);
        Assert.Equal(100_000m, byEmp[minWageEmp].DisbursedAmount);
        Assert.Equal(50_000m, byEmp[pensionEmp].DisbursedAmount);
        Assert.Equal(0m, byEmp[regularEmp].DisbursedAmount);
        Assert.Equal(result.TotalEntitled, result.TotalDisbursed + result.TotalArrears);
    }

    [Fact]
    public void Manual_arrears_edit_blocked_without_four_eyes()
    {
        var actor = Guid.NewGuid();
        var entry = new ArrearsLedgerEntry
        {
            EmployeeId = Guid.NewGuid(),
            PayrollRunId = Guid.NewGuid(),
            OriginalAmount = 100_000m,
            PaidAmount = 60_000m,
            ArrearsAmount = 40_000m,
            Status = ArrearsLedgerStatus.PartiallyPaid
        };

        Assert.False(entry.TryManualUpdate(90_000m, 50_000m, actor, actor));
        Assert.Equal(100_000m, entry.OriginalAmount);
        Assert.Equal(40_000m, entry.ArrearsAmount);

        var approver = Guid.NewGuid();
        Assert.True(entry.TryManualUpdate(90_000m, 50_000m, actor, approver, "Audit correction"));
        Assert.Equal(90_000m, entry.OriginalAmount);
        Assert.Equal(50_000m, entry.PaidAmount);
        Assert.Equal(40_000m, entry.ArrearsAmount);
        Assert.Equal(approver, entry.UpdatedBy);
    }

    [Fact]
    public void ApplyPayment_maintains_invariant_and_rejects_overpay()
    {
        var entry = new ArrearsLedgerEntry
        {
            EmployeeId = Guid.NewGuid(),
            PayrollRunId = Guid.NewGuid(),
            OriginalAmount = 100_000m,
            PaidAmount = 60_000m,
            ArrearsAmount = 40_000m,
            Status = ArrearsLedgerStatus.PartiallyPaid
        };

        Assert.False(entry.ApplyPayment(50_000m));
        Assert.True(entry.ApplyPayment(20_000m));
        Assert.Equal(80_000m, entry.PaidAmount);
        Assert.Equal(20_000m, entry.ArrearsAmount);
        Assert.Equal(entry.PaidAmount + entry.ArrearsAmount, entry.OriginalAmount);
        Assert.Equal(ArrearsLedgerStatus.PartiallyPaid, entry.Status);

        Assert.True(entry.ApplyPayment(20_000m));
        Assert.Equal(ArrearsLedgerStatus.FullyPaid, entry.Status);
        Assert.Equal(0m, entry.ArrearsAmount);
    }

    [Fact]
    public void Doc_and_artifacts_exist()
    {
        var path = RepoPaths.Resolve("docs", "PAY_004_ARREARS.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("PAY-004", content);
        Assert.Matches("(?i)arrears", content);
        Assert.Matches("(?i)partial", content);
        Assert.Matches("(?i)priority", content);

        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Services", "Payroll", "Payroll.Domain", "Entities", "ArrearsLedgerEntry.cs")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Services", "Payroll", "Payroll.Application", "Services", "PartialDisbursementService.cs")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "pay004-validate.ps1")));
    }
}
