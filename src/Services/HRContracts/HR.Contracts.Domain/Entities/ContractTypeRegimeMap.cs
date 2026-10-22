using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

/// <summary>
/// FUN-029 (F1): which contract type a hire gets for a service regime (SYR-003 id) and an employment type.
/// A null <see cref="LegalRegimeId"/> or <see cref="EmploymentType"/> means "any". Configuration, managed on the
/// ContractTypeRegimeMaps screen — production seeds none. Unique per tenant with NULLS NOT DISTINCT (PostgreSQL 15+),
/// so "(law50, any)" can exist once.
/// </summary>
[Display(Name = "ربط نوع العقد بنظام الخدمة")]
public class ContractTypeRegimeMap : AuditableEntity
{
    [Display(Name = "نظام الخدمة")]
    [MaxLength(50)]
    public string? LegalRegimeId { get; set; }

    [Display(Name = "نوع التوظيف")]
    [MaxLength(20)]
    public string? EmploymentType { get; set; }

    [Display(Name = "نوع العقد")]
    [ForeignKey(nameof(ContractType))]
    public Guid ContractTypeId { get; set; }

    [Display(Name = "فعال")]
    public bool IsActive { get; set; } = true;

    public ContractType? ContractType { get; set; }
}
