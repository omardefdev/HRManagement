using HR.Movements.Domain.Entities;
using HR.Movements.Infrastructure.Data;
using HRManagement.ServiceDefaults;

namespace HR.Movements.Infrastructure.OrgScope;

/// <summary>
/// PLAN-52 (P-9): the scope keys of HR.Movements, for the approval screens a delegation opens to a restricted user (PLAN-50). A transfer
/// and a loan carry two units, both of which must be in the scope (as FUN-003 does). The other three movement types carry only an employee,
/// and HR.Movements has no employee snapshot (G-MOV-10, wave 2): they are central for a restricted user (<c>[OrgScopeCentralOnly]</c>).
/// </summary>
public static class MovementsOrgScopeKeys
{
    public const string Service = "hr-movements";

    public static void Configure(OrgScopeKeyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry
            .Key<MovementsDbContext, TransferRequest>(_ => t => (Guid?)t.FromOrgUnitId, _ => t => (Guid?)t.ToOrgUnitId)
            .Key<MovementsDbContext, LoanAssignmentRequest>(_ => l => (Guid?)l.LendingOrgUnitId, _ => l => (Guid?)l.BorrowingOrgUnitId);

        registry
            .Id("transferRequestId", typeof(TransferRequest))
            .Id("fromOrgUnitId", typeof(OrgScopeUnit))
            .Id("toOrgUnitId", typeof(OrgScopeUnit))
            .Id("lendingOrgUnitId", typeof(OrgScopeUnit))
            .Id("borrowingOrgUnitId", typeof(OrgScopeUnit))
            .Id("orgUnitId", typeof(OrgScopeUnit))
            .NotKey("employeeId", "no employee snapshot in HR.Movements (G-MOV-10, wave 2): a transfer's and a loan's two units are their key");
    }
}
