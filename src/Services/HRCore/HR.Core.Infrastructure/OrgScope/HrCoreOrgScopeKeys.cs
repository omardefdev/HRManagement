using System.Linq.Expressions;
using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;

namespace HR.Core.Infrastructure.OrgScope;

/// <summary>
/// PLAN-52 (§2, P-4, P-5): the scope keys of HR.Core. A record's unit is its own column, its employee's <c>PrimaryOrgUnitId</c>, or its
/// parent's; a null unit, or a missing parent, is no key: central (D-52-1). Program.cs passes it to <c>AddOrgScopeFilter</c>; the
/// architecture tests and the translation tests call it for real.
/// </summary>
public static class HrCoreOrgScopeKeys
{
    public const string Service = "hr-core";

    private static readonly Lazy<OrgScopeKeyRegistry> Built = new(() =>
    {
        var registry = new OrgScopeKeyRegistry(Service);
        Configure(registry);
        return registry;
    });

    /// <summary>The registry, for the services that are not controllers (the inbox, the export jobs); Program.cs registers its own copy.</summary>
    public static OrgScopeKeyRegistry Registry => Built.Value;

    private const string FileReason = "a file of the record the action resolves by its own id";

    /// <summary>The unit of an employee, by id: <c>Employee.PrimaryOrgUnitId</c>.</summary>
    private static Expression<Func<Guid?, Guid?>> EmployeeUnit(HRCoreDbContext db) =>
        id => db.Employees.Where(e => (Guid?)e.Id == id).Select(e => e.PrimaryOrgUnitId).FirstOrDefault();

    private static Func<HRCoreDbContext, Expression<Func<T, Guid?>>> ByEmployee<T>(Expression<Func<T, Guid?>> employeeId) =>
        db => OrgScopeExpressions.Through(employeeId, EmployeeUnit(db));

    public static void Configure(OrgScopeKeyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        // The employee and the records that hang on one employee (§2 rule 2: their answers are kept).
        registry
            .Key<HRCoreDbContext, Employee>(_ => e => e.PrimaryOrgUnitId)
            .Key<HRCoreDbContext, OrgUnit>(_ => u => (Guid?)u.Id)
            .Key(ByEmployee<DailyAttendanceRecord>(r => r.EmployeeId))
            .Key(ByEmployee<ContactInfo>(r => r.EmployeeId))
            .Key(ByEmployee<EmployeeDocument>(r => r.EmployeeId))
            .Key(ByEmployee<EmployeeCorrection>(r => r.EmployeeId))
            .Key(ByEmployee<EmployeeProfile>(r => r.EmployeeId))
            .Key(ByEmployee<EmploymentRecord>(r => r.EmployeeId))
            .Key(ByEmployee<PerformanceEvaluation>(r => r.EmployeeId))
            .Key(ByEmployee<Qualification>(r => r.EmployeeId))
            .Key(ByEmployee<EmploymentStatusHistory>(r => r.EmployeeId))
            .Key(ByEmployee<ServiceRegimeException>(r => r.EmployeeId))
            .Key(ByEmployee<ApprovedCV>(r => r.EmployeeId))
            // The legacy approval queue: a request for an approved CV is its employee's; any other target has no key (central).
            .Key<HRCoreDbContext, ApprovalRequest>(db => a => a.EntityType == "ApprovedCV"
                ? db.ApprovedCVs.Where(c => c.Id == a.EntityId)
                    .Select(c => db.Employees.Where(e => e.Id == c.EmployeeId).Select(e => e.PrimaryOrgUnitId).FirstOrDefault())
                    .FirstOrDefault()
                : null);

        // The branch screens (§2): a unit column (Diwan and inspections since HCM052), or the parent's.
        registry
            .Key<HRCoreDbContext, DiwanCorrespondence>(_ => d => d.OrgUnitId)
            .Key<HRCoreDbContext, FieldInspectionVisit>(_ => v => v.OrgUnitId)
            .Key<HRCoreDbContext, HrStatisticReport>(_ => r => r.OrgUnitId)
            .Key<HRCoreDbContext, ShiftRoster>(_ => r => r.OrgUnitId)
            .Key<HRCoreDbContext, ShiftAssignment>(db => a => db.ShiftRosters.Where(r => r.Id == a.ShiftRosterId).Select(r => r.OrgUnitId).FirstOrDefault())
            .Key<HRCoreDbContext, KeyPosition>(_ => k => k.OrgUnitId)
            .Key<HRCoreDbContext, SuccessionPlan>(db => p => db.KeyPositions.Where(k => k.Id == p.KeyPositionId).Select(k => k.OrgUnitId).FirstOrDefault())
            .Key<HRCoreDbContext, SuccessorCandidate>(db => c => db.SuccessionPlans.Where(p => p.Id == c.SuccessionPlanId)
                .Select(p => db.KeyPositions.Where(k => k.Id == p.KeyPositionId).Select(k => k.OrgUnitId).FirstOrDefault())
                .FirstOrDefault());

        // Establishment: the position's and the plan's unit.
        registry
            .Key<HRCoreDbContext, FundedPosition>(_ => p => (Guid?)p.OrgUnitId)
            .Key<HRCoreDbContext, WorkforcePlan>(_ => p => p.OrgUnitId)
            .Key<HRCoreDbContext, WorkforcePlanLine>(db => l => db.WorkforcePlans.Where(p => p.Id == l.WorkforcePlanId).Select(p => p.OrgUnitId).FirstOrDefault());

        // Recruitment ids that the recruitment filter resolves through this registry ([OrgScopeId] on the action), and the appeals.
        registry
            .Key<HRCoreDbContext, RecruitmentRequisition>(db => r => db.FundedPositions.Where(p => p.Id == r.FundedPositionId).Select(p => (Guid?)p.OrgUnitId).FirstOrDefault())
            .Key<HRCoreDbContext, InternalVacancy>(_ => v => v.OrgUnitId)
            .Key<HRCoreDbContext, InternalApplication>(db => a => db.InternalVacancies.Where(v => v.Id == a.InternalVacancyId).Select(v => v.OrgUnitId).FirstOrDefault())
            .Key<HRCoreDbContext, RecruitmentAppeal>(_ => a => (Guid?)a.RecruitmentApplication!.Requisition!.FundedPosition!.OrgUnitId)
            .Key<HRCoreDbContext, RecruitmentInterviewSlot>(db => s => db.RecruitmentApplications.Where(a => a.Id == s.RecruitmentApplicationId)
                .Select(a => (Guid?)a.Requisition!.FundedPosition!.OrgUnitId)
                .FirstOrDefault());

        // Workflow instances: the unit stamped at start (internal types, WorkflowInstanceScopeResolver) or given by the request (external).
        registry.Key<HRCoreDbContext, WorkflowInstance>(_ => w => w.ScopeOrgUnitId);

        // The ids by name, for every controller of the service; an action or a parameter can say otherwise ([OrgScopeId], [NotOrgScopeKey]).
        registry
            .Id("employeeId", typeof(Employee))
            .Id("relatedEmployeeId", typeof(Employee))
            .Id("headEmployeeId", typeof(Employee))
            .Id("currentHolderEmployeeId", typeof(Employee))
            .Id("evaluatorId", typeof(Employee))
            .Id("orgUnitId", typeof(OrgScopeUnit))
            .Id("primaryOrgUnitId", typeof(OrgScopeUnit))
            .Id("scopeOrgUnitId", typeof(OrgScopeUnit))
            .Id("keyPositionId", typeof(KeyPosition))
            .Id("rosterId", typeof(ShiftRoster))
            .Id("shiftRosterId", typeof(ShiftRoster))
            .Id("successionPlanId", typeof(SuccessionPlan))
            .Id("fundedPositionId", typeof(FundedPosition))
            .Id("employmentRecordId", typeof(EmploymentRecord))
            .Id("approvalRequestId", typeof(ApprovalRequest))
            .NotKey("shiftId", "reference data: the shift catalogue (D-52-3)")
            .NotKey("fileId", FileReason)
            .NotKey("photoFileId", FileReason)
            .NotKey("attachmentFileId", FileReason)
            .NotKey("tenantId", "the agency: the tenant filter decides it, not the org scope")
            .NotKey("jobCatalogEntryId", "reference data: the job catalogue")
            .NotKey("legalSourceId", "reference data: the legislation register");
    }
}

/// <summary>PLAN-52: HR.Core's scope, from its own org units (<see cref="OrgScopeService"/>, cached by materialized path).</summary>
public sealed class HrCoreOrgScopeProvider(OrgScopeService orgScope) : IOrgScopeProvider
{
    public Task<OrgAccessScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
        orgScope.GetAllowedOrgUnitIdsAsync(user, ct);
}
