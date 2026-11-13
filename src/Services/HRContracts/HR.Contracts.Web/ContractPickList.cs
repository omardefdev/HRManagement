using HR.Contracts.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Contracts.Web;

/// <summary>
/// PLAN-52 (review 3, P-4): the contracts a form offers (commencement, probation, separation) are those of the caller's scope, through the
/// registry's <c>Contract</c> key (the employee's snapshot; none = central). Null for an unrestricted caller: the pick-list is unchanged.
/// </summary>
public static class ContractPickList
{
    public static async Task<IQueryable<Guid>?> InScopeAsync(HttpContext http)
    {
        var scope = await http.GetOrgScopeAsync();
        if (!scope.MustFilter && !scope.IsDenied)
            return null;
        var db = http.RequestServices.GetRequiredService<ContractsDbContext>();
        var contracts = await db.Contracts.AsNoTracking().InOrgScopeAsync(http, db);
        return contracts.Select(c => c.Id);
    }
}
