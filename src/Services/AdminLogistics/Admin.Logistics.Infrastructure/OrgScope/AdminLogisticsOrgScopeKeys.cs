using System.Linq.Expressions;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Infrastructure.OrgScope;

/// <summary>
/// PLAN-52 (§4, P-8): the scope keys of Admin.Logistics. A supply order carries its unit, a receipt is its order's; an asset's unit is its
/// active custodian's (the logic of <c>AssetOrgScopeGuard</c>: an assignment in force, its employee's local snapshot), a maintenance record
/// is its asset's, a custody record is its employee's. No unit (an asset nobody holds, an employee without a snapshot) is no key: central
/// (D-52-1). Program.cs passes it to <c>AddOrgScopeFilter</c>; the architecture tests and the translation tests call it for real.
/// </summary>
public static class AdminLogisticsOrgScopeKeys
{
    public const string Service = "admin-logistics";

    private static readonly Lazy<OrgScopeKeyRegistry> Built = new(() =>
    {
        var registry = new OrgScopeKeyRegistry(Service);
        Configure(registry);
        return registry;
    });

    /// <summary>The registry, for code that is not a controller; Program.cs registers its own copy.</summary>
    public static OrgScopeKeyRegistry Registry => Built.Value;

    private static Expression<Func<Guid?, Guid?>> EmployeeUnit(AdminLogisticsDbContext db) =>
        id => db.EmployeeOrgUnitSnapshots.Where(s => (Guid?)s.EmployeeId == id).Select(s => s.PrimaryOrgUnitId).FirstOrDefault();

    /// <summary>An asset's unit: its custodian's, through the assignment in force (active, pending approval or starting approval).</summary>
    private static Expression<Func<Guid?, Guid?>> AssetUnit(AdminLogisticsDbContext db) =>
        assetId => db.AssetAssignments
            .Where(a => (Guid?)a.AssetId == assetId
                        && a.ReturnedAt == null
                        && (a.Status == AssetAssignmentStatus.Active
                            || a.Status == AssetAssignmentStatus.PendingApproval
                            || a.Status == AssetAssignmentStatus.ApprovalStarting))
            .Select(a => db.EmployeeOrgUnitSnapshots.Where(s => s.EmployeeId == a.EmployeeId).Select(s => s.PrimaryOrgUnitId).FirstOrDefault())
            .FirstOrDefault();

    public static void Configure(OrgScopeKeyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry
            .KeyBy<AdminLogisticsDbContext, EmployeeOrgUnitSnapshot>(_ => s => s.EmployeeId, _ => s => s.PrimaryOrgUnitId)
            .Key<AdminLogisticsDbContext, SupplyOrder>(_ => o => o.OrgUnitId)
            .Key<AdminLogisticsDbContext, SupplyOrderLine>(db => l => db.SupplyOrders.Where(o => o.Id == l.SupplyOrderId).Select(o => o.OrgUnitId).FirstOrDefault())
            .Key<AdminLogisticsDbContext, ReceiptRecord>(db => r => db.SupplyOrders.Where(o => (Guid?)o.Id == r.SupplyOrderId).Select(o => o.OrgUnitId).FirstOrDefault())
            .Key<AdminLogisticsDbContext, Asset>(db => OrgScopeExpressions.Through<Asset>(a => a.Id, AssetUnit(db)))
            .Key<AdminLogisticsDbContext, MaintenanceRecord>(db => OrgScopeExpressions.Through<MaintenanceRecord>(m => m.AssetId, AssetUnit(db)))
            .Key<AdminLogisticsDbContext, AssetLossReport>(db => OrgScopeExpressions.Through<AssetLossReport>(r => r.AssetId, AssetUnit(db)))
            .Key<AdminLogisticsDbContext, AssetAssignment>(db => OrgScopeExpressions.Through<AssetAssignment>(a => a.EmployeeId, EmployeeUnit(db)));

        registry
            .Id("employeeId", typeof(EmployeeOrgUnitSnapshot))
            .Id("holderEmployeeId", typeof(EmployeeOrgUnitSnapshot))
            .Id("assetId", typeof(Asset))
            .Id("assignmentId", typeof(AssetAssignment))
            .Id("supplyOrderId", typeof(SupplyOrder))
            .Id("supplyOrderLineId", typeof(SupplyOrderLine))
            .Id("receiptId", typeof(ReceiptRecord))
            .Id("originReceiptRecordId", typeof(ReceiptRecord))
            .Id("orgUnitId", typeof(OrgScopeUnit))
            .NotKey("inventoryItemId", "reference data: the inventory items (D-52-3)")
            .NotKey("requestId", "the asset's own approval request: the action finds it under the asset it resolved by id")
            .NotKey("handoverFileId", "a file of the record the action resolves by its own id")
            .NotKey("returnFileId", "a file of the record the action resolves by its own id")
            .NotKey("lossFileId", "a file of the record the action resolves by its own id")
            .NotKey("tenantId", "the agency: the tenant filter decides it, not the org scope");
    }
}
