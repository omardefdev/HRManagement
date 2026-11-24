using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

/// <summary>GOV-002-D: dated electronic legal attestation for a config version hash.</summary>
[Display(Name = "تصديق قانوني")]
public class LegalAttestation : BaseEntity
{
    public Guid PayrollConfigurationId { get; set; }

    public PayrollConfiguration? PayrollConfiguration { get; set; }

    public Guid AttestedBy { get; set; }

    public DateTime AttestedAt { get; set; }

    /// <summary>JSON array of decisionId strings.</summary>
    [Required]
    public string DecisionIdsJson { get; set; } = "[]";

    [Required, MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>PLAN-54: JSON array of the STR-002 exception ids the attested version's interim values rely on.</summary>
    [Required]
    public string InterimExceptionIdsJson { get; set; } = "[]";

    /// <summary>Active | Voided</summary>
    [Required, MaxLength(32)]
    public string Status { get; set; } = "Active";
}
