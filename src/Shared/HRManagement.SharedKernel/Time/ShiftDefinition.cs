using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Time;

/// <summary>HCM-002: defines a work shift with start/end times, break, and overtime rules.</summary>
public class ShiftDefinition : BaseEntity
{
    [Required, MaxLength(50)]
    public string ShiftCode { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string ShiftName { get; set; } = string.Empty;

    public ShiftType Type { get; set; } = ShiftType.Regular;

    /// <summary>Shift start time of day (e.g. 08:00).</summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>Shift end time of day (e.g. 16:00). May be next day for night shifts.</summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>Break duration in minutes.</summary>
    public int BreakMinutes { get; set; }

    /// <summary>Whether this shift crosses midnight (night shift).</summary>
    public bool CrossesMidnight { get; set; }

    /// <summary>Minimum minutes before overtime starts.</summary>
    public int OvertimeThresholdMinutes { get; set; }

    /// <summary>Maximum overtime minutes allowed per day.</summary>
    public int MaxOvertimeMinutes { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Net expected working minutes = total span minus break.</summary>
    public int ExpectedWorkMinutes
    {
        get
        {
            var span = CrossesMidnight
                ? (TimeSpan.FromHours(24) - StartTime + EndTime)
                : (EndTime - StartTime);
            return (int)span.TotalMinutes - BreakMinutes;
        }
    }

    public bool IsEffectiveOn(DateTime date) =>
        IsActive && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo.Value);
}

public enum ShiftType
{
    [Display(Name = "اعتيادي")]
    Regular = 0,
    [Display(Name = "ليلية")]
    Night = 1,
    [Display(Name = "مقسّمة")]
    Split = 2,
    [Display(Name = "مستمر")]
    Continuous = 3,
    [Display(Name = "مرن")]
    Flexible = 4
}
