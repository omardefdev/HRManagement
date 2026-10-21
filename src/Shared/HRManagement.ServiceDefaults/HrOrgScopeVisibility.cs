using System.ComponentModel.DataAnnotations;
using System.Security.Claims;



namespace HRManagement.ServiceDefaults;



/// <summary>

/// Explicit org-unit access scope (IAM-002). Never use null to mean "all access".

/// </summary>

public sealed class OrgAccessScope

{

    public bool IsUnrestricted { get; }

    /// <summary>Branch-scoped user with missing/invalid claim — deny everything.</summary>

    public bool IsDenied { get; }

    public IReadOnlySet<Guid> AllowedOrgUnitIds { get; }



    private OrgAccessScope(bool unrestricted, bool denied, IReadOnlySet<Guid>? ids)

    {

        IsUnrestricted = unrestricted;

        IsDenied = denied;

        AllowedOrgUnitIds = ids ?? new HashSet<Guid>();

    }



    public static OrgAccessScope Unrestricted() => new(true, false, null);



    public static OrgAccessScope Restricted(IReadOnlySet<Guid> ids) =>

        new(false, false, ids ?? new HashSet<Guid>());



    /// <summary>Fail-closed: branch manager without a valid scope claim.</summary>

    public static OrgAccessScope Denied() => new(false, true, new HashSet<Guid>());



    public bool CanAccess(Guid? orgUnitId)

    {

        if (IsDenied)

            return false;

        if (IsUnrestricted)

            return true;

        return orgUnitId.HasValue && AllowedOrgUnitIds.Contains(orgUnitId.Value);

    }



    /// <summary>True when list queries must restrict to AllowedOrgUnitIds (or empty when Denied).</summary>

    public bool MustFilter => !IsUnrestricted;

}



public enum OrgScopeClaimKind

{

    /// <summary>Admin / hr-core / non-branch roles — unrestricted.</summary>

    [Display(Name = "غير محدد النطاق")]
    NotScoped,

    /// <summary>Branch manager with a valid scope_org_unit_id claim.</summary>

    [Display(Name = "ساري")]
    Valid,

    /// <summary>Branch manager missing or invalid claim — fail-closed.</summary>

    [Display(Name = "مطالبة غير صالحة")]
    InvalidClaim

}



public readonly record struct OrgScopeClaimResult(OrgScopeClaimKind Kind, Guid? RootOrgUnitId)

{

    public static OrgScopeClaimResult NotScoped { get; } = new(OrgScopeClaimKind.NotScoped, null);

    public static OrgScopeClaimResult InvalidClaim { get; } = new(OrgScopeClaimKind.InvalidClaim, null);

    public static OrgScopeClaimResult Valid(Guid rootOrgUnitId) => new(OrgScopeClaimKind.Valid, rootOrgUnitId);

}



/// <summary>

/// Hierarchical org-unit data scope within a tenant (TEN-001 / IAM-002):

/// branch users see their unit and descendants; central roles see all within the tenant.

/// Branch managers without a valid claim are denied (fail-closed).

/// Tenant boundary is enforced separately via <see cref="HRManagement.SharedKernel.Tenancy.ITenantContext"/> / <c>tenant_id</c>.

/// </summary>

public static class HrOrgScopeVisibility

{

    public const string ScopeOrgUnitClaimType = "scope_org_unit_id";



    public static bool IsBranchScoped(ClaimsPrincipal user) =>

        user.IsInRole(HrManagementRoles.BranchManager)

        && !user.IsInRole(HrManagementRoles.Admin);



    /// <summary>

    /// Resolves the branch scope claim. Prefer this over <see cref="GetScopeRootOrgUnitId"/> —

    /// InvalidClaim must become 403, not unrestricted access.

    /// </summary>

    public static OrgScopeClaimResult GetScopeClaimResult(ClaimsPrincipal user)

    {

        if (!IsBranchScoped(user))

            return OrgScopeClaimResult.NotScoped;



        var value = user.FindFirst(ScopeOrgUnitClaimType)?.Value;

        if (Guid.TryParse(value, out var id))

            return OrgScopeClaimResult.Valid(id);



        return OrgScopeClaimResult.InvalidClaim;

    }



    /// <summary>

    /// Returns the scope root when Valid; null for NotScoped (unrestricted) or InvalidClaim.

    /// Prefer <see cref="GetScopeClaimResult"/> for fail-closed enforcement.

    /// </summary>

    public static Guid? GetScopeRootOrgUnitId(ClaimsPrincipal user)

    {

        var result = GetScopeClaimResult(user);

        return result.Kind == OrgScopeClaimKind.Valid ? result.RootOrgUnitId : null;

    }



    public static Guid? GetEmployeeId(ClaimsPrincipal user)

    {

        var value = user.FindFirst("employee_id")?.Value ?? user.FindFirst("employeeId")?.Value;

        return Guid.TryParse(value, out var id) ? id : null;

    }



    public static bool CanAccessOrgUnit(OrgAccessScope scope, Guid? orgUnitId) =>

        scope.CanAccess(orgUnitId);



    /// <summary>Legacy overload: null set means unrestricted (Admin path only).</summary>

    public static bool CanAccessOrgUnit(IReadOnlySet<Guid>? allowed, Guid? orgUnitId)

    {

        if (allowed is null)

            return true;

        if (!orgUnitId.HasValue)

            return false;

        return allowed.Contains(orgUnitId.Value);

    }

}


