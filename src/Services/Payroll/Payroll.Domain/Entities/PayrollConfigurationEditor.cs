using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-54 (§3): one person who shaped a framework version's content: created it, imported a pack into it, or edited it. The record
/// only grows; the separation of duties reads it (an editor never approves, attests or publishes the version). It is not part of the
/// fingerprint: it says who, not what.
/// </summary>
[Display(Name = "محرر نسخة الإطار")]
public class PayrollConfigurationEditor : BaseEntity
{
    public Guid PayrollConfigurationId { get; set; }

    public Guid UserId { get; set; }

    public PayrollFrameworkEditKind Kind { get; set; }

    public DateTime At { get; set; }

    public PayrollConfiguration PayrollConfiguration { get; set; } = null!;
}
