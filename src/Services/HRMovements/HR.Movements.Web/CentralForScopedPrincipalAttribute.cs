using HR.Movements.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HR.Movements.Web;

/// <summary>
/// PLAN-52 (review 1, P-9): the movement types that carry only an employee (secondment, promotion, delegation) are central for a restricted
/// user, because HR.Movements has no employee snapshot (<see cref="OrgScopeCentralOnlyAttribute"/> reads the caller's own scope). A delegate
/// admitted through a delegation whose principal was branch-scoped acts within that principal's stored scope (PLAN-50), so these screens are
/// central for him too: 403, whatever his own scope. A delegation of a central principal, and a caller admitted by his own role, pass.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class CentralForScopedPrincipalAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
    public int Order => OrgScopeActionFilter<MovementsDbContext>.FilterOrder;

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (MovementApprovalActing.AdmittedBy(context.HttpContext)?.PrincipalScopeOrgUnitId is not null)
        {
            context.Result = new ForbidResult();
            return Task.CompletedTask;
        }

        return next();
    }
}
