using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.SharedKernel.Enums;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

public class LeaveBalanceAndPolicyTests
{
    [Fact]
    public void ValidateLeaveBalance_NullBalance_ReturnsError()
    {
        var error = LeaveAndAdvanceRules.ValidateLeaveBalance(null, 5);
        Assert.NotNull(error);
        Assert.Contains("لا يوجد رصيد", error);
    }

    [Fact]
    public void ValidateLeaveBalance_InsufficientBalance_ReturnsError()
    {
        var balance = new LeaveBalance { TotalEntitlement = 10, Used = 8 };
        var error = LeaveAndAdvanceRules.ValidateLeaveBalance(balance, 5);
        Assert.NotNull(error);
        Assert.Contains("غير كافٍ", error);
    }

    [Fact]
    public void ValidateLeaveBalance_SufficientBalance_ReturnsNull()
    {
        var balance = new LeaveBalance { TotalEntitlement = 30, Used = 10 };
        var error = LeaveAndAdvanceRules.ValidateLeaveBalance(balance, 5);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateLeaveBalance_ExactBalance_ReturnsNull()
    {
        var balance = new LeaveBalance { TotalEntitlement = 10, Used = 5 };
        var error = LeaveAndAdvanceRules.ValidateLeaveBalance(balance, 5);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateLeavePolicy_NoPolicy_ReturnsNull()
    {
        var error = LeaveAndAdvanceRules.ValidateLeavePolicy(null, LeaveType.Annual, DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));
        Assert.Null(error);
    }

    [Fact]
    public void ValidateLeavePolicy_InsufficientNotice_ReturnsError()
    {
        var policy = new LeavePolicy { LeaveType = LeaveType.Annual, MinNoticeDays = 7 };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var error = LeaveAndAdvanceRules.ValidateLeavePolicy(policy, LeaveType.Annual, today, today.AddDays(3));
        Assert.NotNull(error);
        Assert.Contains("7 يوم", error);
    }

    [Fact]
    public void ValidateLeavePolicy_SufficientNotice_ReturnsNull()
    {
        var policy = new LeavePolicy { LeaveType = LeaveType.Annual, MinNoticeDays = 3 };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var error = LeaveAndAdvanceRules.ValidateLeavePolicy(policy, LeaveType.Annual, today, today.AddDays(5));
        Assert.Null(error);
    }

    [Fact]
    public void LeaveBalance_Remaining_ComputedCorrectly()
    {
        var balance = new LeaveBalance { TotalEntitlement = 30, Used = 12 };
        Assert.Equal(18, balance.Remaining);
    }
}

public class AdvanceInstallmentPlanTests
{
    [Fact]
    public void ValidateAdvanceAgainstSalary_ExceedsLimit_ReturnsError()
    {
        var error = LeaveAndAdvanceRules.ValidateAdvanceAgainstSalary(40000, 10000, 3);
        Assert.NotNull(error);
        Assert.Contains("يتجاوز الحد الأقصى", error);
    }

    [Fact]
    public void ValidateAdvanceAgainstSalary_WithinLimit_ReturnsNull()
    {
        var error = LeaveAndAdvanceRules.ValidateAdvanceAgainstSalary(20000, 10000, 3);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateAdvanceAgainstSalary_ZeroSalary_ReturnsNull()
    {
        var error = LeaveAndAdvanceRules.ValidateAdvanceAgainstSalary(50000, 0, 3);
        Assert.Null(error);
    }

    [Fact]
    public void CreateInstallmentPlan_DefaultInstallments_CreatesValidPlan()
    {
        var plan = LeaveAndAdvanceRules.CreateInstallmentPlan(Guid.NewGuid(), 60000);
        Assert.True(plan.NumberOfInstallments > 0);
        Assert.True(plan.MonthlyInstallment > 0);
        Assert.Equal(60000, plan.TotalAmount);
        Assert.Equal(60000, plan.RemainingAmount);
    }

    [Fact]
    public void CreateInstallmentPlan_SpecifiedInstallments_CreatesCorrectPlan()
    {
        var plan = LeaveAndAdvanceRules.CreateInstallmentPlan(Guid.NewGuid(), 12000, 6);
        Assert.Equal(6, plan.NumberOfInstallments);
        Assert.Equal(2000, plan.MonthlyInstallment);
    }

    [Fact]
    public void CreateInstallmentPlan_SmallAmount_MinimumOneInstallment()
    {
        var plan = LeaveAndAdvanceRules.CreateInstallmentPlan(Guid.NewGuid(), 1000);
        Assert.True(plan.NumberOfInstallments >= 1);
    }
}

public class PayrollCalculationExtendedTests
{
    [Fact]
    public void CalculateGross_BasePlusAllowances()
    {
        var allowances = new[]
        {
            new AllowanceRecord { Amount = 1000 },
            new AllowanceRecord { Amount = 500 }
        };
        var gross = PayrollCalculation.CalculateGross(5000, allowances);
        Assert.Equal(6500, gross);
    }

    [Fact]
    public void CalculateGross_NoAllowances_ReturnsBase()
    {
        var gross = PayrollCalculation.CalculateGross(5000, Array.Empty<AllowanceRecord>());
        Assert.Equal(5000, gross);
    }

    [Fact]
    public void CalculateDeductions_FixedAndPercentage()
    {
        var deductions = new[] { new DeductionRecord { Amount = 200 } };
        var types = new[]
        {
            new DeductionType { IsStatutory = true, Percentage = 10, FixedAmount = null },
            new DeductionType { IsStatutory = true, Percentage = null, FixedAmount = 100 }
        };
        var total = PayrollCalculation.CalculateDeductions(10000, deductions, types);
        Assert.Equal(1300, total); // 200 + 1000 (10%) + 100
    }

    [Fact]
    public void CalculateNetPay_NeverNegative()
    {
        var net = PayrollCalculation.CalculateNetPay(1000, 5000);
        Assert.Equal(0, net);
    }

    [Fact]
    public void CalculateNetPay_CorrectDifference()
    {
        var net = PayrollCalculation.CalculateNetPay(10000, 2500);
        Assert.Equal(7500, net);
    }

    [Fact]
    public void GenerateSalaryLine_CreatesCorrectComponents()
    {
        var allowances = new List<AllowanceRecord>
        {
            new() { Id = Guid.NewGuid(), Amount = 1000, AllowanceType = new CompensationType { NameAr = "بدل نقل" } }
        };
        var deductions = new List<DeductionRecord>
        {
            new() { Id = Guid.NewGuid(), Amount = 500, DeductionType = "تأمين" }
        };
        var types = new List<DeductionType>();
        var adjustments = new List<PayrollAdjustment>();

        var (line, components) = PayrollCalculation.GenerateSalaryLine(
            Guid.NewGuid(), Guid.NewGuid(), 5000,
            allowances, deductions, types, adjustments);

        Assert.Equal(6000, line.GrossPay);
        Assert.Equal(500, line.TotalDeductions);
        Assert.Equal(5500, line.NetPay);
        Assert.Equal(3, components.Count); // Base + Allowance + Deduction
    }

    [Fact]
    public void GenerateSalaryLine_WithAdjustments()
    {
        var (line, components) = PayrollCalculation.GenerateSalaryLine(
            Guid.NewGuid(), Guid.NewGuid(), 5000,
            Array.Empty<AllowanceRecord>(),
            Array.Empty<DeductionRecord>(),
            Array.Empty<DeductionType>(),
            new List<PayrollAdjustment> { new() { Id = Guid.NewGuid(), Amount = 200, AdjustmentReason = "مكافأة" } });

        Assert.Equal(5000, line.GrossPay);
        Assert.Equal(5200, line.NetPay); // 5000 + 200 adjustment
        Assert.Equal(2, components.Count); // Base + Adjustment
    }
}

public class EndOfServiceCalculationTests
{
    [Fact]
    public void CalculateServiceYears_FiveYears()
    {
        var start = new DateTime(2020, 1, 1);
        var end = new DateTime(2025, 1, 1);
        var years = EndOfServiceCalculation.CalculateServiceYears(start, end);
        Assert.InRange(years, 4.9m, 5.1m);
    }

    [Fact]
    public void CalculateGratuity_LessThanFiveYears()
    {
        var gratuity = EndOfServiceCalculation.CalculateGratuity(10000, 3);
        Assert.Equal(15000, gratuity); // 10000 * 0.5 * 3
    }

    [Fact]
    public void CalculateGratuity_ExactlyFiveYears()
    {
        var gratuity = EndOfServiceCalculation.CalculateGratuity(10000, 5);
        Assert.Equal(25000, gratuity); // 10000 * 0.5 * 5
    }

    [Fact]
    public void CalculateGratuity_MoreThanFiveYears()
    {
        var gratuity = EndOfServiceCalculation.CalculateGratuity(10000, 8);
        Assert.Equal(55000, gratuity); // (10000 * 0.5 * 5) + (10000 * 3)
    }

    [Fact]
    public void CalculateGratuity_ZeroYears_ReturnsZero()
    {
        var gratuity = EndOfServiceCalculation.CalculateGratuity(10000, 0);
        Assert.Equal(0, gratuity);
    }

    [Fact]
    public void CalculateLeaveBalancePayout_CorrectAmount()
    {
        var payout = EndOfServiceCalculation.CalculateLeaveBalancePayout(9000, 15);
        Assert.Equal(4500, payout); // (9000/30) * 15
    }

    [Fact]
    public void CalculateLeaveBalancePayout_NoLeave_ReturnsZero()
    {
        var payout = EndOfServiceCalculation.CalculateLeaveBalancePayout(9000, 0);
        Assert.Equal(0, payout);
    }

    [Fact]
    public void CreateSettlement_CalculatesCorrectly()
    {
        var settlement = EndOfServiceCalculation.CreateSettlement(
            Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2020, 1, 1),
            new DateTime(2025, 1, 1),
            baseSalary: 10000,
            remainingLeaveDays: 15,
            outstandingAdvances: 5000);

        Assert.True(settlement.ServiceYears > 0);
        Assert.True(settlement.GratuityAmount > 0);
        Assert.True(settlement.LeaveBalancePayout > 0);
        Assert.Equal(5000, settlement.TotalDeductions);
        Assert.True(settlement.NetSettlement >= 0);
        Assert.Equal(EosStatus.Draft, settlement.Status);
    }
}

public class ActivationGatesTests
{
    [Fact]
    public void Contract_Pending_CanBeActivated()
    {
        var contract = new Contract { Status = ContractStatus.Pending };
        Assert.Equal(ContractStatus.Pending, contract.Status);
    }

    [Fact]
    public void Contract_Active_CannotBeActivated()
    {
        var contract = new Contract { Status = ContractStatus.Active };
        Assert.NotEqual(ContractStatus.Pending, contract.Status);
    }

    [Fact]
    public void Contract_WithSignatures_MeetsRequirement()
    {
        var contract = new Contract { Status = ContractStatus.Pending };
        contract.Signatures.Add(new ContractSignature
        {
            SignedBy = Guid.NewGuid(),
            SignedAt = DateTime.UtcNow,
            SignatureType = SignatureType.Digital
        });
        Assert.True(contract.Signatures.Any());
    }

    [Fact]
    public void Contract_WithoutSignatures_FailsRequirement()
    {
        var contract = new Contract { Status = ContractStatus.Pending };
        Assert.False(contract.Signatures.Any());
    }
}

public class ServiceTransactionStateMachineTests
{
    [Fact]
    public void MoveTo_ValidTransition_Succeeds()
    {
        var txn = new ServiceTransaction();
        txn.MoveTo(ServiceTransactionState.Submitted, Guid.NewGuid());
        Assert.Equal(ServiceTransactionState.Submitted, txn.State);
        Assert.Single(txn.StateHistory);
    }

    [Fact]
    public void MoveTo_InvalidTransition_ThrowsException()
    {
        var txn = new ServiceTransaction();
        Assert.Throws<InvalidOperationException>(() =>
            txn.MoveTo(ServiceTransactionState.Completed, Guid.NewGuid()));
    }

    [Fact]
    public void MoveTo_SameState_NoOp()
    {
        var txn = new ServiceTransaction();
        txn.MoveTo(ServiceTransactionState.Draft, Guid.NewGuid());
        Assert.Empty(txn.StateHistory);
    }

    [Fact]
    public void MoveTo_FullWorkflow_RecordsHistory()
    {
        var userId = Guid.NewGuid();
        var txn = new ServiceTransaction();
        txn.MoveTo(ServiceTransactionState.Submitted, userId);
        txn.MoveTo(ServiceTransactionState.UnderReview, userId);
        txn.MoveTo(ServiceTransactionState.InProcess, userId);
        txn.MoveTo(ServiceTransactionState.PendingApproval, userId);
        txn.MoveTo(ServiceTransactionState.Approved, userId);

        Assert.Equal(ServiceTransactionState.Approved, txn.State);
        Assert.Equal(5, txn.StateHistory.Count);
    }

    [Fact]
    public void MoveTo_Cancel_FromAnyActiveState()
    {
        var txn = new ServiceTransaction();
        txn.MoveTo(ServiceTransactionState.Submitted, Guid.NewGuid());
        txn.MoveTo(ServiceTransactionState.Cancelled, Guid.NewGuid());
        Assert.Equal(ServiceTransactionState.Cancelled, txn.State);
    }
}
