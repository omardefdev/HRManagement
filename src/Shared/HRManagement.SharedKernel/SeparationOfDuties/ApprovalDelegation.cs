using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>
/// Temporary, dated transfer of approval authority from a principal to a delegate (IAM-003). PLAN-50 (D-WF-5, D-50-1, D-50-2): one role
/// (<see cref="RoleScope"/>, mandatory, delegable, held directly by the principal at creation), an optional entity type from a closed list,
/// at most 90 days, no chain. The delegate is an employee of the directory (<see cref="DelegateEmployeeId"/>, matched with the
/// <c>employee_id</c> claim); rows older than PLAN-50 that hold only <see cref="DelegateId"/> are matched with <c>sub</c>. A delegation
/// never reaches a token or <c>IsInRole</c>: it acts only at the named decision points.
/// </summary>
public class ApprovalDelegation : BaseEntity
{
    /// <summary>The principal's user id (<c>sub</c>).</summary>
    [Required]
    public Guid PrincipalId { get; set; }

    /// <summary>The principal's employee id (the <c>employee_id</c> claim at creation), when the token carries one.</summary>
    public Guid? PrincipalEmployeeId { get; set; }

    [StringLength(200)]
    public string? PrincipalName { get; set; }

    /// <summary>
    /// PLAN-50 (P-5): the principal's org-scope root (the <c>scope_org_unit_id</c> claim of a branch-scoped principal) at creation; null for
    /// a central principal. A delegate never gets a wider scope than this.
    /// </summary>
    public Guid? PrincipalScopeOrgUnitId { get; set; }

    /// <summary>The delegate's user id (<c>sub</c>): rows older than PLAN-50 only.</summary>
    public Guid? DelegateId { get; set; }

    /// <summary>PLAN-50 (D-50-2): the delegate, an employee chosen from HR.Core's directory.</summary>
    public Guid? DelegateEmployeeId { get; set; }

    [StringLength(200)]
    public string? DelegateName { get; set; }

    /// <summary>Null means all entity types; otherwise one of <c>Delegation:EntityTypes</c>.</summary>
    [StringLength(128)]
    public string? EntityTypeScope { get; set; }

    /// <summary>PLAN-50 (D-WF-5): the delegated role, mandatory. A row with none (older than PLAN-50) was revoked by the migration.</summary>
    [StringLength(100)]
    public string? RoleScope { get; set; }

    public DateTime ActiveFrom { get; set; }

    public DateTime ActiveUntil { get; set; }

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    public bool IsRevoked { get; set; }

    public Guid? RevokedBy { get; set; }

    public DateTime? RevokedAt { get; set; }

    [StringLength(500)]
    public string? RevokedReason { get; set; }
}

/// <summary>
/// PLAN-50 (§5): a delegation in force for one actor, as a lookup returns it: whose authority (<see cref="PrincipalUserId"/>), which role,
/// which entity type (null = all), until when, and the principal's stored scope. It is never a role of the actor.
/// </summary>
public sealed record ActiveDelegation(
    Guid Id,
    Guid PrincipalUserId,
    Guid? PrincipalEmployeeId,
    string? PrincipalName,
    string Role,
    string? EntityTypeScope,
    DateTime ActiveUntil,
    Guid? PrincipalScopeOrgUnitId = null);
