using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.SharedKernel.Enums;

namespace HRManagement.UnitTests;

public class LeaveAndAdvanceRulesTests
{
    [Fact]
    public void ValidateLeaveRequest_rejects_end_before_start()
    {
        var err = LeaveAndAdvanceRules.ValidateLeaveRequest(
            Guid.NewGuid(),
            new DateOnly(2026, 5, 10),
            new DateOnly(2026, 5, 1),
            1,
            [],
            null);
        Assert.NotNull(err);
    }

    [Fact]
    public void ValidateLeaveRequest_rejects_duration_mismatch()
    {
        // Mon 2026-05-04 .. Wed 2026-05-06 => 3 working days
        var err = LeaveAndAdvanceRules.ValidateLeaveRequest(
            Guid.NewGuid(),
            new DateOnly(2026, 5, 4),
            new DateOnly(2026, 5, 6),
            2,
            [],
            null);
        Assert.NotNull(err);
    }

    [Fact]
    public void ValidateLeaveRequest_accepts_working_day_duration()
    {
        var start = new DateOnly(2026, 5, 4);
        var end = new DateOnly(2026, 5, 6);
        var duration = LeaveAndAdvanceRules.ComputeLeaveDurationDays(start, end);
        var err = LeaveAndAdvanceRules.ValidateLeaveRequest(Guid.NewGuid(), start, end, duration, [], null);
        Assert.Null(err);
    }

    [Fact]
    public void ValidateLeaveRequest_rejects_overlap_approved()
    {
        var emp = Guid.NewGuid();
        var existing = new List<LeaveRequest>
        {
            new()
            {
                Id = Guid.NewGuid(),
                EmployeeId = emp,
                StartDate = new DateOnly(2026, 6, 1),
                EndDate = new DateOnly(2026, 6, 10),
                DurationDays = 10,
                Status = ApprovalStatus.Approved
            }
        };

        var err = LeaveAndAdvanceRules.ValidateLeaveRequest(
            emp,
            new DateOnly(2026, 6, 8),
            new DateOnly(2026, 6, 10),
            LeaveAndAdvanceRules.ComputeLeaveDurationDays(new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 10)),
            existing,
            null);
        Assert.NotNull(err);
    }

    [Fact]
    public void ValidateAdvanceRequest_rejects_second_pending()
    {
        var emp = Guid.NewGuid();
        var adv = new List<AdvanceRequest>
        {
            new() { Id = Guid.NewGuid(), EmployeeId = emp, Amount = 100, Status = ApprovalStatus.Pending }
        };

        var err = LeaveAndAdvanceRules.ValidateAdvanceRequest(200, 10_000, adv, null);
        Assert.NotNull(err);
    }

    [Fact]
    public void ValidateLeaveRequest_rejects_exact_boundary_overlap()
    {
        var emp = Guid.NewGuid();
        var existing = new List<LeaveRequest>
        {
            new()
            {
                Id = Guid.NewGuid(),
                EmployeeId = emp,
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 8, 10),
                DurationDays = 10,
                Status = ApprovalStatus.Approved
            }
        };

        var err = LeaveAndAdvanceRules.ValidateLeaveRequest(
            emp,
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 14),
            LeaveAndAdvanceRules.ComputeLeaveDurationDays(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 14)),
            existing,
            null);
        Assert.NotNull(err);
    }

    [Fact]
    public void CreateInstallmentPlan_installments_sum_to_total()
    {
        const decimal total = 10_000m;
        const int installments = 3;
        var plan = LeaveAndAdvanceRules.CreateInstallmentPlan(Guid.NewGuid(), total, installments);
        var scheduled = Enumerable.Range(0, installments)
            .Select(i => LeaveAndAdvanceRules.GetInstallmentAmount(total, installments, i))
            .ToList();
        Assert.Equal(total, scheduled.Sum());
        Assert.Equal(scheduled[0], plan.MonthlyInstallment);
    }

    [Fact]
    public void CreateInstallmentPlan_respects_minimum_one_installment()
    {
        var plan = LeaveAndAdvanceRules.CreateInstallmentPlan(Guid.NewGuid(), 100m);
        Assert.Equal(1, plan.NumberOfInstallments);
        Assert.Equal(100m, plan.MonthlyInstallment);
        Assert.Equal(100m, plan.RemainingAmount);
    }

    [Fact]
    public void ValidateAdvanceRequest_rejects_amount_above_max_single()
    {
        var err = LeaveAndAdvanceRules.ValidateAdvanceRequest(
            LeaveAndAdvanceRules.DefaultMaxSingleAdvance + 1m,
            LeaveAndAdvanceRules.DefaultMaxSingleAdvance,
            [],
            null);
        Assert.NotNull(err);
    }
}
