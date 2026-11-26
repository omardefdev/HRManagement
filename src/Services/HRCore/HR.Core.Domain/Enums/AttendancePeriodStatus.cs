using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>
/// PLAN-57 (§1, rule 8): the state of an attendance period, for display. The lock is per employee (a summary in a period that
/// covers the day), never this value.
/// </summary>
public enum AttendancePeriodStatus
{
    [Display(Name = "مفتوحة")]
    Open = 0,
    [Display(Name = "مغلقة جزئياً")]
    PartiallyClosed = 1,
    [Display(Name = "مغلقة")]
    Closed = 2
}
