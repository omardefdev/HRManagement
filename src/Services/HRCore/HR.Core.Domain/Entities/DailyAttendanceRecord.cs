using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class DailyAttendanceRecord : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "اليوم")]
    public DateOnly AttendanceDate { get; set; }

    [Display(Name = "الحالة")]
    public DailyAttendanceStatus Status { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public virtual Employee? Employee { get; set; }
}
