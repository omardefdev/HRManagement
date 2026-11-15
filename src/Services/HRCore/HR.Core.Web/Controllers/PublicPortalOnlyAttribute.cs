using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc.ActionConstraints;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-67 (D-67-5, G-UX-07): the public careers portal lives at <c>/careers</c> alone. Behind the gateway every other request of HR.Core
/// carries the module's path base (<c>/ui/hr-core</c>), and the portal's routes sit at HR.Core's root, so without this constraint
/// <c>/ui/hr-core/</c> opened the citizens' portal instead of the dashboard. A portal action accepts a request that came through
/// <c>/careers</c> (the path base <see cref="ProductionHostExtensions.UseHrConfiguredPathBase"/> sets), or one with no path base at all
/// (the service reached directly, as the in-process tests do); it refuses the module's path base, so the conventional route
/// (<c>{controller=Dashboard}</c>) answers there.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PublicPortalOnlyAttribute : Attribute, IActionConstraint
{
    public int Order => 0;

    public bool Accept(ActionConstraintContext context)
    {
        var pathBase = context.RouteContext.HttpContext.Request.PathBase;
        return !pathBase.HasValue || pathBase.StartsWithSegments(ProductionHostExtensions.PublicCareersGatewayPrefix);
    }
}
