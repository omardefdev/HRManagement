using System.Linq.Expressions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Infrastructure.OrgScope;

/// <summary>
/// PLAN-52 (§3, P-7): the scope keys of HR.Contracts. Every employee record is keyed through its <c>EmployeeId</c> to the local
/// <see cref="EmployeeOrgUnitSnapshot"/>; an employee without a snapshot, or a snapshot without a unit, is no key: central (D-52-1, the plan's
/// §3 rule 2). Program.cs passes it to <c>AddOrgScopeFilter</c>; the export jobs, the architecture tests and the translation tests call it for
/// real.
/// </summary>
public static class ContractsOrgScopeKeys
{
    public const string Service = "hr-contracts";

    private static readonly Lazy<OrgScopeKeyRegistry> Built = new(() =>
    {
        var registry = new OrgScopeKeyRegistry(Service);
        Configure(registry);
        return registry;
    });

    /// <summary>The registry, for the services that are not controllers (the export jobs); Program.cs registers its own copy.</summary>
    public static OrgScopeKeyRegistry Registry => Built.Value;

    /// <summary>The unit of an employee, by id: his snapshot's <c>PrimaryOrgUnitId</c>.</summary>
    private static Expression<Func<Guid?, Guid?>> EmployeeUnit(ContractsDbContext db) =>
        id => db.EmployeeOrgUnitSnapshots.Where(s => (Guid?)s.EmployeeId == id).Select(s => s.PrimaryOrgUnitId).FirstOrDefault();

    private static Func<ContractsDbContext, Expression<Func<T, Guid?>>> ByEmployee<T>(Expression<Func<T, Guid?>> employeeId) =>
        db => OrgScopeExpressions.Through(employeeId, EmployeeUnit(db));

    public static void Configure(OrgScopeKeyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry
            .KeyBy<ContractsDbContext, EmployeeOrgUnitSnapshot>(_ => s => s.EmployeeId, _ => s => s.PrimaryOrgUnitId)
            .Key(ByEmployee<ServiceTransaction>(t => t.EmployeeId))
            .Key(ByEmployee<Contract>(c => c.EmployeeId))
            .Key(ByEmployee<LeaveBalance>(b => b.EmployeeId))
            .Key(ByEmployee<LeaveRequest>(r => r.EmployeeId))
            .Key(ByEmployee<AdvanceRequest>(r => r.EmployeeId))
            .Key(ByEmployee<CommencementRecord>(r => r.EmployeeId))
            .Key(ByEmployee<ProbationRecord>(r => r.EmployeeId))
            .Key(ByEmployee<SeparationRecord>(r => r.EmployeeId))
            // A decision names its employee when it has one; a decision without one is central.
            .Key(ByEmployee<AdministrativeDecision>(d => d.RelatedEmployeeId))
            .Key<ContractsDbContext, ContractSignature>(db => s => db.Contracts.Where(c => c.Id == s.ContractId)
                .Select(c => db.EmployeeOrgUnitSnapshots.Where(e => e.EmployeeId == c.EmployeeId).Select(e => e.PrimaryOrgUnitId).FirstOrDefault())
                .FirstOrDefault());

        registry
            .Id("employeeId", typeof(EmployeeOrgUnitSnapshot))
            .Id("relatedEmployeeId", typeof(EmployeeOrgUnitSnapshot))
            .Id("transactionId", typeof(ServiceTransaction))
            .Id("serviceTransactionId", typeof(ServiceTransaction))
            .Id("contractId", typeof(Contract))
            .NotKey("contractTypeId", "reference data: the contract types")
            .NotKey("fileId", "a file of the record the action resolves by its own id")
            .Id("orgUnitId", typeof(OrgScopeUnit))
            .NotKey("tenantId", "the agency: the tenant filter decides it, not the org scope");
    }
}
