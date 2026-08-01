using CoreEnums = HR.Core.Domain.Enums;
using CoreServices = HR.Core.Domain.Services;
using TrainingEnums = HR.Training.Domain.Enums;
using TrainingServices = HR.Training.Domain.Services;

namespace HRManagement.UnitTests;

public class ShiftAllowanceRulesTests
{
    [Fact]
    public void ComputeShiftHours_Handles_Overnight_Wrap()
    {
        // 00:00 → 08:00 ليلية = 8 ساعات
        Assert.Equal(8m, CoreServices.ShiftAllowanceRules.ComputeShiftHours(new(0, 0), new(8, 0)));
        // 16:00 → 00:00 مسائية = 8 ساعات (تعبر منتصف الليل)
        Assert.Equal(8m, CoreServices.ShiftAllowanceRules.ComputeShiftHours(new(16, 0), new(0, 0)));
    }

    [Fact]
    public void Night_Shift_Applies_25_Percent_Premium()
    {
        var result = CoreServices.ShiftAllowanceRules.Compute(
            CoreEnums.ShiftType.Night, new(0, 0), new(8, 0), overtimeHours: 0, hourlyWage: 100m);

        Assert.Equal(8m, result.ShiftHours);
        Assert.Equal(0.25m, result.PremiumPercent);
        Assert.Equal(200m, result.ShiftPremiumAmount); // 8 * 100 * 0.25
        Assert.Equal(200m, result.TotalAllowance);
    }

    [Fact]
    public void Day_Shift_Has_No_Premium_But_Overtime_Counts()
    {
        var result = CoreServices.ShiftAllowanceRules.Compute(
            CoreEnums.ShiftType.Day, new(8, 0), new(16, 0), overtimeHours: 2, hourlyWage: 100m);

        Assert.Equal(0m, result.ShiftPremiumAmount);
        Assert.Equal(300m, result.OvertimeAmount); // 2 * 100 * 1.5
        Assert.Equal(300m, result.TotalAllowance);
    }

    [Fact]
    public void Negative_Inputs_Are_Clamped()
    {
        var result = CoreServices.ShiftAllowanceRules.Compute(
            CoreEnums.ShiftType.Evening, new(16, 0), new(0, 0), overtimeHours: -5, hourlyWage: -50m);

        Assert.Equal(0m, result.OvertimeAmount);
        Assert.Equal(0m, result.ShiftPremiumAmount);
    }
}

public class SuccessionReadinessRulesTests
{
    [Fact]
    public void High_Score_And_Tenure_Is_ReadyNow()
    {
        var level = CoreServices.SuccessionReadinessRules.Classify(90m, 5, meetsQualification: true);
        Assert.Equal(CoreEnums.ReadinessLevel.ReadyNow, level);
    }

    [Fact]
    public void Medium_Score_Is_ReadyWithinYear()
    {
        var level = CoreServices.SuccessionReadinessRules.Classify(75m, 1, meetsQualification: true);
        Assert.Equal(CoreEnums.ReadinessLevel.ReadyWithinYear, level);
    }

    [Fact]
    public void High_Score_But_Short_Tenure_Is_ReadyWithinYear()
    {
        var level = CoreServices.SuccessionReadinessRules.Classify(90m, 1, meetsQualification: true);
        Assert.Equal(CoreEnums.ReadinessLevel.ReadyWithinYear, level);
    }

    [Fact]
    public void Missing_Qualification_Forces_NeedsDevelopment()
    {
        var level = CoreServices.SuccessionReadinessRules.Classify(95m, 10, meetsQualification: false);
        Assert.Equal(CoreEnums.ReadinessLevel.NeedsDevelopment, level);
    }

    [Fact]
    public void Low_Score_Is_NeedsDevelopment()
    {
        var level = CoreServices.SuccessionReadinessRules.Classify(40m, 8, meetsQualification: true);
        Assert.Equal(CoreEnums.ReadinessLevel.NeedsDevelopment, level);
    }
}

public class LearningCompletionRulesTests
{
    [Fact]
    public void Progress_Maps_To_Status()
    {
        Assert.Equal(TrainingEnums.LearningEnrollmentStatus.Enrolled, TrainingServices.LearningCompletionRules.StatusFor(0));
        Assert.Equal(TrainingEnums.LearningEnrollmentStatus.InProgress, TrainingServices.LearningCompletionRules.StatusFor(50));
        Assert.Equal(TrainingEnums.LearningEnrollmentStatus.Completed, TrainingServices.LearningCompletionRules.StatusFor(100));
    }

    [Fact]
    public void IsComplete_Only_At_100()
    {
        Assert.False(TrainingServices.LearningCompletionRules.IsComplete(99));
        Assert.True(TrainingServices.LearningCompletionRules.IsComplete(100));
    }

    [Fact]
    public void ClampProgress_Bounds_To_0_100()
    {
        Assert.Equal(0, TrainingServices.LearningCompletionRules.ClampProgress(-10));
        Assert.Equal(100, TrainingServices.LearningCompletionRules.ClampProgress(150));
        Assert.Equal(60, TrainingServices.LearningCompletionRules.ClampProgress(60));
    }

    [Fact]
    public void Certificate_Number_Is_Deterministic_And_Formatted()
    {
        var employeeId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var date = new DateOnly(2026, 6, 12);

        var first = TrainingServices.LearningCompletionRules.GenerateCertificateNumber("gov-101", employeeId, date);
        var second = TrainingServices.LearningCompletionRules.GenerateCertificateNumber("gov-101", employeeId, date);

        Assert.Equal(first, second);
        Assert.StartsWith("CERT-GOV-101-20260612-", first);
    }
}
