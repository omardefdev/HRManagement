using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Core.Domain.Services;
using HRManagement.SharedKernel.Enums;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

#region PayrollEngine Tests

public class PayrollEngine_CalculateTaxTests
{
    [Fact]
    public void CalculateTax_ZeroIncome_ReturnsZero() =>
        Assert.Equal(0m, PayrollEngine.CalculateTax(0m));

    [Fact]
    public void CalculateTax_NegativeIncome_ReturnsZero() =>
        Assert.Equal(0m, PayrollEngine.CalculateTax(-1000m));

    [Fact]
    public void CalculateTax_WithinExemptBracket_ReturnsZero() =>
        Assert.Equal(0m, PayrollEngine.CalculateTax(4000m));

    [Fact]
    public void CalculateTax_SingleBracket_CorrectAmount()
    {
        // 10_000: first 5000 at 0%, next 5000 at 5% = 250
        Assert.Equal(250m, PayrollEngine.CalculateTax(10_000m));
    }

    [Fact]
    public void CalculateTax_MultipleBrackets_CorrectAmount()
    {
        // 25_000: 5000@0% + 10000@5% + 10000@10% = 0 + 500 + 1000 = 1500
        Assert.Equal(1500m, PayrollEngine.CalculateTax(25_000m));
    }

    [Fact]
    public void CalculateTax_AllBrackets_CorrectAmount()
    {
        // 60_000: 5000@0% + 10000@5% + 15000@10% + 20000@15% + 10000@20%
        // = 0 + 500 + 1500 + 3000 + 2000 = 7000
        Assert.Equal(7000m, PayrollEngine.CalculateTax(60_000m));
    }

    [Fact]
    public void CalculateTax_CustomBrackets()
    {
        var brackets = new[]
        {
            new PayrollEngine.TaxBracketEntry(10_000m, 0.10m),
            new PayrollEngine.TaxBracketEntry(decimal.MaxValue, 0.20m),
        };
        // 15_000: 10000@10% + 5000@20% = 1000 + 1000 = 2000
        Assert.Equal(2000m, PayrollEngine.CalculateTax(15_000m, brackets));
    }

    [Fact]
    public void CalculateTax_EmptyBrackets_ReturnsZero() =>
        Assert.Equal(0m, PayrollEngine.CalculateTax(10_000m, Array.Empty<PayrollEngine.TaxBracketEntry>()));
}

public class PayrollEngine_CalculateEndOfServiceRealTests
{
    [Fact]
    public void LessThanFiveYears_HalfSalaryPerYear()
    {
        var hire = new DateTime(2020, 1, 1);
        var sep = new DateTime(2023, 1, 1); // ~3 years
        var result = PayrollEngine.CalculateEndOfServiceReal(hire, sep, 10_000m);
        var years = EndOfServiceCalculation.CalculateServiceYears(hire, sep);
        var expected = EndOfServiceCalculation.CalculateGratuity(10_000m, years);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MoreThanFiveYears_MixedRate()
    {
        var hire = new DateTime(2015, 1, 1);
        var sep = new DateTime(2023, 1, 1); // ~8 years
        var result = PayrollEngine.CalculateEndOfServiceReal(hire, sep, 10_000m);
        Assert.True(result > 0);
    }

    [Fact]
    public void ZeroService_ReturnsZero()
    {
        var date = new DateTime(2023, 1, 1);
        Assert.Equal(0m, PayrollEngine.CalculateEndOfServiceReal(date, date, 10_000m));
    }
}

public class PayrollEngine_BuildSalaryLinesTests
{
    [Fact]
    public void BuildSalaryLines_BaseSalaryOnly_IncludesTaxComponent()
    {
        var (line, components) = PayrollEngine.BuildSalaryLines(
            Guid.NewGuid(), Guid.NewGuid(), 20_000m,
            Array.Empty<AllowanceRecord>(),
            Array.Empty<DeductionRecord>(),
            Array.Empty<DeductionType>(),
            Array.Empty<PayrollAdjustment>());

        Assert.Contains(components, c => c.ComponentType == SalaryComponentType.Tax);
        Assert.True(line.TotalDeductions > 0);
        Assert.True(line.NetPay < 20_000m);
    }

    [Fact]
    public void BuildSalaryLines_WithAllowances_GrossIncludesAllowances()
    {
        var allowances = new[]
        {
            new AllowanceRecord { Amount = 3000m, AllowanceType = new CompensationType { NameAr = "بدل سكن" } }
        };

        var (line, components) = PayrollEngine.BuildSalaryLines(
            Guid.NewGuid(), Guid.NewGuid(), 10_000m,
            allowances,
            Array.Empty<DeductionRecord>(),
            Array.Empty<DeductionType>(),
            Array.Empty<PayrollAdjustment>());

        Assert.Equal(13_000m, line.GrossPay);
        Assert.Contains(components, c => c.ComponentType == SalaryComponentType.BaseSalary);
        Assert.Contains(components, c => c.ComponentType == SalaryComponentType.Allowance);
    }

    [Fact]
    public void BuildSalaryLines_BelowTaxThreshold_NoTaxComponent()
    {
        var (_, components) = PayrollEngine.BuildSalaryLines(
            Guid.NewGuid(), Guid.NewGuid(), 4000m,
            Array.Empty<AllowanceRecord>(),
            Array.Empty<DeductionRecord>(),
            Array.Empty<DeductionType>(),
            Array.Empty<PayrollAdjustment>());

        Assert.DoesNotContain(components, c => c.ComponentType == SalaryComponentType.Tax);
    }
}

public class PayrollRunStateGuardsTests
{
    [Fact]
    public void ValidateCanModifyLines_WhenDraft_ReturnsNull()
    {
        var run = new PayrollRun { Status = PayrollRunStatus.Draft };
        Assert.Null(run.ValidateCanModifyLines());
    }

    [Fact]
    public void ValidateCanModifyLines_WhenUnderReview_ReturnsError()
    {
        var run = new PayrollRun { Status = PayrollRunStatus.UnderReview };
        Assert.NotNull(run.ValidateCanModifyLines());
    }
}

#endregion

#region LeaveAndAdvanceRules Tests

public class LeavePolicyRules_CalculateWorkingDaysTests
{
    [Fact]
    public void FullWeek_ExcludesFridaySaturday()
    {
        // Mon 2026-05-04 to Sun 2026-05-10 -> Mon-Thu = 4 working days, Fri-Sat excluded, Sun = 1
        var start = new DateOnly(2026, 5, 4);
        var end = new DateOnly(2026, 5, 10);
        Assert.Equal(5, LeaveAndAdvanceRules.CalculateWorkingDays(start, end));
    }

    [Fact]
    public void SingleWorkingDay()
    {
        var monday = new DateOnly(2026, 5, 4);
        Assert.Equal(1, LeaveAndAdvanceRules.CalculateWorkingDays(monday, monday));
    }

    [Fact]
    public void WeekendOnly_ReturnsZero()
    {
        var friday = new DateOnly(2026, 5, 8);
        var saturday = new DateOnly(2026, 5, 9);
        Assert.Equal(0, LeaveAndAdvanceRules.CalculateWorkingDays(friday, saturday));
    }

    [Fact]
    public void EndBeforeStart_ReturnsZero()
    {
        Assert.Equal(0, LeaveAndAdvanceRules.CalculateWorkingDays(
            new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 4)));
    }
}

public class LeavePolicyRules_GetMaxDurationTests
{
    [Theory]
    [InlineData(LeaveType.Annual, 30)]
    [InlineData(LeaveType.Sick, 15)]
    [InlineData(LeaveType.Maternity, 90)]
    [InlineData(LeaveType.Unpaid, 60)]
    [InlineData(LeaveType.Other, 30)]
    public void ReturnsCorrectMaxDuration(LeaveType type, int expected) =>
        Assert.Equal(expected, LeaveAndAdvanceRules.GetMaxDuration(type));
}

public class LeavePolicyRules_ValidateMaxDurationTests
{
    [Fact]
    public void WithinLimit_ReturnsNull() =>
        Assert.Null(LeaveAndAdvanceRules.ValidateMaxDuration(LeaveType.Annual, 25));

    [Fact]
    public void ExceedsLimit_ReturnsError() =>
        Assert.NotNull(LeaveAndAdvanceRules.ValidateMaxDuration(LeaveType.Sick, 20));
}

public class LeavePolicyRules_ValidateLeaveBalanceTests
{
    [Fact]
    public void SufficientBalance_ReturnsNull()
    {
        var balance = new LeaveBalance { EmployeeId = Guid.NewGuid(), LeaveType = LeaveType.Annual, Year = 2026, TotalEntitlement = 30, Used = 5 };
        Assert.Null(LeaveAndAdvanceRules.ValidateLeaveBalance(balance, 10));
    }

    [Fact]
    public void InsufficientBalance_ReturnsError()
    {
        var balance = new LeaveBalance { EmployeeId = Guid.NewGuid(), LeaveType = LeaveType.Annual, Year = 2026, TotalEntitlement = 30, Used = 25 };
        Assert.NotNull(LeaveAndAdvanceRules.ValidateLeaveBalance(balance, 10));
    }

    [Fact]
    public void NullBalance_ReturnsError() =>
        Assert.NotNull(LeaveAndAdvanceRules.ValidateLeaveBalance(null, 5));
}

#endregion

#region AdvancePolicyRules Tests

public class AdvancePolicyRules_ValidateAdvanceAgainstSalaryTests
{
    [Fact]
    public void WithinLimit_ReturnsNull() =>
        Assert.Null(LeaveAndAdvanceRules.ValidateAdvanceAgainstSalary(15_000m, 10_000m, 3m));

    [Fact]
    public void ExceedsLimit_ReturnsError() =>
        Assert.NotNull(LeaveAndAdvanceRules.ValidateAdvanceAgainstSalary(40_000m, 10_000m, 3m));

    [Fact]
    public void ZeroSalary_ReturnsNull() =>
        Assert.Null(LeaveAndAdvanceRules.ValidateAdvanceAgainstSalary(10_000m, 0m));
}

public class AdvancePolicyRules_ValidateNoOpenAdvanceTests
{
    [Fact]
    public void NoOpenAdvance_ReturnsNull()
    {
        var advances = new List<AdvanceRequest>();
        var plans = new List<AdvanceInstallmentPlan>();
        Assert.Null(LeaveAndAdvanceRules.ValidateNoOpenAdvance(advances, plans, null));
    }

    [Fact]
    public void OpenAdvanceExists_ReturnsError()
    {
        var adv = new AdvanceRequest { EmployeeId = Guid.NewGuid(), Amount = 10_000m, Status = ApprovalStatus.Approved };
        typeof(HRManagement.SharedKernel.Entities.BaseEntity).GetProperty("Id")!.SetValue(adv, Guid.NewGuid());
        var plan = new AdvanceInstallmentPlan { AdvanceRequestId = adv.Id, RemainingAmount = 5000m };

        Assert.NotNull(LeaveAndAdvanceRules.ValidateNoOpenAdvance(
            new[] { adv }, new[] { plan }, null));
    }

    [Fact]
    public void ExcludedAdvance_ReturnsNull()
    {
        var adv = new AdvanceRequest { EmployeeId = Guid.NewGuid(), Amount = 10_000m, Status = ApprovalStatus.Approved };
        typeof(HRManagement.SharedKernel.Entities.BaseEntity).GetProperty("Id")!.SetValue(adv, Guid.NewGuid());
        var plan = new AdvanceInstallmentPlan { AdvanceRequestId = adv.Id, RemainingAmount = 5000m };

        Assert.Null(LeaveAndAdvanceRules.ValidateNoOpenAdvance(
            new[] { adv }, new[] { plan }, adv.Id));
    }
}

public class AdvancePolicyRules_ValidateInstallmentRatioTests
{
    [Fact]
    public void WithinRatio_ReturnsNull() =>
        Assert.Null(LeaveAndAdvanceRules.ValidateAdvanceInstallmentRatio(12_000m, 12, 10_000m));

    [Fact]
    public void ExceedsRatio_ReturnsError() =>
        Assert.NotNull(LeaveAndAdvanceRules.ValidateAdvanceInstallmentRatio(30_000m, 3, 10_000m));
}

public class AdvancePolicyRules_CalculateInstallmentsTests
{
    [Fact]
    public void EvenDivision_ReturnsCorrectAmount() =>
        Assert.Equal(2500m, LeaveAndAdvanceRules.CalculateInstallments(10_000m, 4));

    [Fact]
    public void OddDivision_Rounds() =>
        Assert.Equal(3333.33m, LeaveAndAdvanceRules.CalculateInstallments(10_000m, 3));

    [Fact]
    public void ZeroInstallments_DefaultsToOne() =>
        Assert.Equal(10_000m, LeaveAndAdvanceRules.CalculateInstallments(10_000m, 0));
}

#endregion

#region SeparationWorkflowRules Tests

public class SeparationWorkflowRulesTests
{
    private static SeparationRecord MakeRecord(bool published = false) =>
        new() { EmployeeId = Guid.NewGuid(), EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow), SeparationEventPublished = published };

    private static List<SeparationChecklist> MakeChecklist(bool allCompleted)
    {
        return Enum.GetValues<SeparationStep>().Select(s => new SeparationChecklist
        {
            SeparationRecordId = Guid.NewGuid(),
            Step = s,
            IsCompleted = allCompleted,
            CompletedAt = allCompleted ? DateTime.UtcNow : null
        }).ToList();
    }

    [Fact]
    public void CanComplete_AllStepsCompleted_ReturnsTrue() =>
        Assert.True(SeparationWorkflowRules.CanComplete(MakeRecord(), MakeChecklist(true)));

    [Fact]
    public void CanComplete_NotAllStepsCompleted_ReturnsFalse() =>
        Assert.False(SeparationWorkflowRules.CanComplete(MakeRecord(), MakeChecklist(false)));

    [Fact]
    public void CanComplete_AlreadyPublished_ReturnsFalse() =>
        Assert.False(SeparationWorkflowRules.CanComplete(MakeRecord(published: true), MakeChecklist(true)));

    [Fact]
    public void CanComplete_EmptyChecklist_ReturnsFalse() =>
        Assert.False(SeparationWorkflowRules.CanComplete(MakeRecord(), new List<SeparationChecklist>()));

    [Fact]
    public void CanComplete_OnlyOptionalPendingSteps_ReturnsTrue()
    {
        var steps = MakeChecklist(true);
        steps[1].IsRequired = false;
        steps[1].IsCompleted = false;
        steps[1].CompletedAt = null;

        Assert.True(SeparationWorkflowRules.CanComplete(MakeRecord(), steps));
    }

    [Fact]
    public void GetIncompleteRequiredSteps_IgnoresOptionalSteps()
    {
        var steps = MakeChecklist(true);
        steps[0].IsCompleted = false;
        steps[0].CompletedAt = null;
        steps[1].IsRequired = false;
        steps[1].IsCompleted = false;
        steps[1].CompletedAt = null;

        var incomplete = SeparationWorkflowRules.GetIncompleteRequiredSteps(steps);

        Assert.Single(incomplete);
        Assert.Equal(steps[0].Step, incomplete[0].Step);
    }

    [Fact]
    public void CanCancel_NotPublished_ReturnsTrue() =>
        Assert.True(SeparationWorkflowRules.CanCancel(MakeRecord()));

    [Fact]
    public void CanCancel_Published_ReturnsFalse() =>
        Assert.False(SeparationWorkflowRules.CanCancel(MakeRecord(published: true)));

    [Fact]
    public void GetNextPendingStep_ReturnsPending()
    {
        var steps = MakeChecklist(true);
        steps[2].IsCompleted = false;
        var next = SeparationWorkflowRules.GetNextPendingStep(steps);
        Assert.NotNull(next);
        Assert.Equal(steps[2].Step, next.Step);
    }

    [Fact]
    public void GetNextPendingStep_AllCompleted_ReturnsNull()
    {
        var next = SeparationWorkflowRules.GetNextPendingStep(MakeChecklist(true));
        Assert.Null(next);
    }

    [Fact]
    public void GetNextPendingStep_SkipsOptionalPendingStep()
    {
        var steps = MakeChecklist(true);
        steps[1].IsRequired = false;
        steps[1].IsCompleted = false;
        steps[2].IsCompleted = false;
        steps[2].CompletedAt = null;

        var next = SeparationWorkflowRules.GetNextPendingStep(steps);

        Assert.NotNull(next);
        Assert.Equal(steps[2].Step, next!.Step);
    }
}

#endregion

#region EmploymentStatusRules Tests

public class EmploymentStatusRulesTests
{
    [Theory]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.OnLeave)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Suspended)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Terminated)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Retired)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Resigned)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Seconded)]
    [InlineData(EmploymentStatus.OnLeave, EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.OnLeave, EmploymentStatus.Terminated)]
    [InlineData(EmploymentStatus.Suspended, EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.Suspended, EmploymentStatus.Terminated)]
    [InlineData(EmploymentStatus.Seconded, EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.Seconded, EmploymentStatus.Terminated)]
    public void AllowedTransitions_ReturnsTrue(EmploymentStatus from, EmploymentStatus to) =>
        Assert.True(EmploymentStatusRules.CanTransition(from, to));

    [Theory]
    [InlineData(EmploymentStatus.Terminated, EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.Retired, EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.Resigned, EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.Terminated, EmploymentStatus.OnLeave)]
    [InlineData(EmploymentStatus.Anonymized, EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.Merged, EmploymentStatus.Active)]
    public void DisallowedTransitions_ReturnsFalse(EmploymentStatus from, EmploymentStatus to) =>
        Assert.False(EmploymentStatusRules.CanTransition(from, to));

    [Fact]
    public void SameStatus_ReturnsFalse() =>
        Assert.False(EmploymentStatusRules.CanTransition(EmploymentStatus.Active, EmploymentStatus.Active));

    [Theory]
    [InlineData(EmploymentStatus.Terminated, true)]
    [InlineData(EmploymentStatus.Retired, true)]
    [InlineData(EmploymentStatus.Resigned, true)]
    [InlineData(EmploymentStatus.Active, false)]
    [InlineData(EmploymentStatus.OnLeave, false)]
    [InlineData(EmploymentStatus.Suspended, false)]
    [InlineData(EmploymentStatus.Seconded, false)]
    public void IsTerminal_ReturnsExpected(EmploymentStatus status, bool expected) =>
        Assert.Equal(expected, EmploymentStatusRules.IsTerminal(status));
}

#endregion
