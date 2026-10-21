using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Controller helpers for fail-closed org scope (IAM-002).
/// </summary>
public static class OrgScopeControllerExtensions
{
    /// <summary>
    /// Returns Forbid when scope is denied; otherwise null (continue).
    /// </summary>
    public static IActionResult? ForbidIfOrgScopeDenied(this ControllerBase controller, OrgAccessScope scope)
    {
        if (!scope.IsDenied)
            return null;

        OrgScopeDenialContext.Record(
            controller.HttpContext,
            controller.User,
            reason: "org-scope-denied");
        return controller.Forbid();
    }

    /// <summary>
    /// Returns Forbid when the org unit is outside scope (or scope is denied).
    /// </summary>
    public static IActionResult? ForbidIfOrgUnitOutOfScope(
        this ControllerBase controller,
        OrgAccessScope scope,
        Guid? orgUnitId,
        string? resourceType = null,
        string? resourceId = null)
    {
        if (!scope.IsDenied && scope.CanAccess(orgUnitId))
            return null;

        OrgScopeDenialContext.Record(
            controller.HttpContext,
            controller.User,
            reason: "org-scope-denied",
            resourceType: resourceType ?? (orgUnitId.HasValue ? "OrgUnit" : null),
            resourceId: resourceId ?? orgUnitId?.ToString("D"));
        return controller.Forbid();
    }



    /// <summary>

    /// Applies org-unit filter to a query when scope is restricted or denied.

    /// Denied scopes produce an empty result set.

    /// </summary>

    public static IQueryable<T> WhereInOrgScope<T>(

        this IQueryable<T> query,

        OrgAccessScope scope,

        System.Linq.Expressions.Expression<Func<T, Guid?>> orgUnitIdSelector)

    {

        if (!scope.MustFilter)

            return query;



        var ids = scope.AllowedOrgUnitIds;

        // Denied → empty set → no rows

        var param = orgUnitIdSelector.Parameters[0];

        var body = orgUnitIdSelector.Body;

        // e => e.OrgUnitId.HasValue && ids.Contains(e.OrgUnitId.Value)

        var hasValue = System.Linq.Expressions.Expression.Property(body, nameof(Nullable<Guid>.HasValue));

        var value = System.Linq.Expressions.Expression.Property(body, nameof(Nullable<Guid>.Value));

        var containsMethod = typeof(IReadOnlySet<Guid>).GetMethod(nameof(IReadOnlySet<Guid>.Contains), [typeof(Guid)])!;

        var contains = System.Linq.Expressions.Expression.Call(

            System.Linq.Expressions.Expression.Constant(ids),

            containsMethod,

            value);

        var and = System.Linq.Expressions.Expression.AndAlso(hasValue, contains);

        var lambda = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(and, param);

        return query.Where(lambda);

    }



    /// <summary>

    /// Non-nullable Guid org unit id filter.

    /// </summary>

    public static IQueryable<T> WhereInOrgScope<T>(

        this IQueryable<T> query,

        OrgAccessScope scope,

        System.Linq.Expressions.Expression<Func<T, Guid>> orgUnitIdSelector)

    {

        if (!scope.MustFilter)

            return query;



        var ids = scope.AllowedOrgUnitIds;

        var param = orgUnitIdSelector.Parameters[0];

        var body = orgUnitIdSelector.Body;

        var containsMethod = typeof(IReadOnlySet<Guid>).GetMethod(nameof(IReadOnlySet<Guid>.Contains), [typeof(Guid)])!;

        var contains = System.Linq.Expressions.Expression.Call(

            System.Linq.Expressions.Expression.Constant(ids),

            containsMethod,

            body);

        var lambda = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(contains, param);

        return query.Where(lambda);

    }

}


