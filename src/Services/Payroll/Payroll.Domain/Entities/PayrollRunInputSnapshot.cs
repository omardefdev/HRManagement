using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

/// <summary>PAY-003 / PERF-001: frozen employee input version at snapshot time.</summary>
[Display(Name = "لقطة مدخلات تشغيل")]
public class PayrollRunInputSnapshot : BaseEntity
{
    public Guid PayrollRunJobId { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid? EmployeePayProfileId { get; set; }

    /// <summary>Opaque version token (UpdatedAt + salary + grade) for conflict detection.</summary>
    [Required, MaxLength(128)]
    public string ProfileVersion { get; set; } = string.Empty;

    /// <summary>PERF-001: SHA-256 hex of allowances/deductions/adjustments/absences at snapshot.</summary>
    [MaxLength(64)]
    public string? InputFingerprint { get; set; }

    public Guid? PayrollConfigurationId { get; set; }

    [MaxLength(64)]
    public string? ConfigContentHash { get; set; }

    /// <summary>
    /// PLAN-55 (D-PAY-15): the employee's <c>Active</c> bank account at the snapshot; the run's line copies it. It is not an input of the
    /// amount: it is in neither <see cref="InputFingerprint"/> nor <see cref="ProfileVersion"/>.
    /// </summary>
    public Guid? BankAccountId { get; set; }

    public int SortOrder { get; set; }

    public PayrollRunJob Job { get; set; } = null!;

    public EmployeeBankAccount? BankAccount { get; set; }
}
