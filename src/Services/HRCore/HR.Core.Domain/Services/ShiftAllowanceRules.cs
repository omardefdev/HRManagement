using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>
/// قواعد احتساب بدلات المناوبات (ليلي/مسائي) والعمل الإضافي.
/// دالة صرفة قابلة للاختبار دون اعتماد على قاعدة البيانات أو خدمة الرواتب.
/// </summary>
public static class ShiftAllowanceRules
{
    /// <summary>نسبة البدل الليلي من أجر ساعات الوردية.</summary>
    public const decimal NightPremiumPercent = 0.25m;

    /// <summary>نسبة البدل المسائي من أجر ساعات الوردية.</summary>
    public const decimal EveningPremiumPercent = 0.10m;

    /// <summary>مضاعِف أجر ساعة العمل الإضافي.</summary>
    public const decimal OvertimeMultiplier = 1.5m;

    /// <summary>عدد ساعات الوردية مع معالجة العبور بعد منتصف الليل.</summary>
    public static decimal ComputeShiftHours(TimeOnly start, TimeOnly end)
    {
        var minutes = (end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        if (minutes <= 0)
            minutes += TimeSpan.FromDays(1).TotalMinutes; // وردية تعبر منتصف الليل
        return Math.Round((decimal)(minutes / 60.0), 2);
    }

    public static decimal PremiumPercentFor(ShiftType type) => type switch
    {
        ShiftType.Night => NightPremiumPercent,
        ShiftType.Evening => EveningPremiumPercent,
        _ => 0m
    };

    /// <summary>
    /// يحسب تفصيل البدل لإسناد وردية بناءً على نوعها وأوقاتها وساعات العمل الإضافي وأجر الساعة.
    /// </summary>
    public static ShiftAllowanceResult Compute(
        ShiftType type,
        TimeOnly start,
        TimeOnly end,
        decimal overtimeHours,
        decimal hourlyWage)
    {
        if (hourlyWage < 0) hourlyWage = 0;
        if (overtimeHours < 0) overtimeHours = 0;

        var shiftHours = ComputeShiftHours(start, end);
        var premiumPercent = PremiumPercentFor(type);

        var shiftPremium = Math.Round(shiftHours * hourlyWage * premiumPercent, 2);
        var overtimeAmount = Math.Round(overtimeHours * hourlyWage * OvertimeMultiplier, 2);

        return new ShiftAllowanceResult(
            shiftHours,
            premiumPercent,
            shiftPremium,
            overtimeHours,
            overtimeAmount,
            shiftPremium + overtimeAmount);
    }
}

public record ShiftAllowanceResult(
    decimal ShiftHours,
    decimal PremiumPercent,
    decimal ShiftPremiumAmount,
    decimal OvertimeHours,
    decimal OvertimeAmount,
    decimal TotalAllowance);
