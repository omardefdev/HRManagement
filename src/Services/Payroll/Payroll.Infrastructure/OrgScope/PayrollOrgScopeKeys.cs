using HRManagement.ServiceDefaults;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.OrgScope;

/// <summary>
/// PLAN-53 (§2 rule 3, P-9; the IAM study's note 3): the branch-scope keys of Payroll. Payroll has no employee snapshot: an employee's
/// unit is the unit of his active pay profile (the consumers keep it current), and a salary change request copies it when it is made.
/// An employee with no active profile, or a profile with no unit, has no key: he is central, and a restricted user never reaches him
/// (D-52-1). Scales and grades belong to the agency, so their ids are not keys. The other payroll screens stay latent rows of the
/// org-scope list (IAM-H).
/// </summary>
public static class PayrollOrgScopeKeys
{
    public const string Service = "payroll";

    /// <summary>The registry, for code that filters outside a request (a test, a job).</summary>
    public static OrgScopeKeyRegistry Registry { get; } = Build();

    public static void Configure(OrgScopeKeyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry
            // A request keeps the unit its employee had when it was made.
            .Key<PayrollDbContext, PayProfileChangeRequest>(_ => r => r.OrgUnitId)
            // A pay profile is reached by its employee: every version of an employee follows the unit of the active one.
            .KeyBy<PayrollDbContext, EmployeePayProfile>(
                _ => p => p.EmployeeId,
                db => p => db.EmployeePayProfiles
                    .Where(active => active.EmployeeId == p.EmployeeId && active.IsActive)
                    .Select(active => active.OrgUnitId)
                    .FirstOrDefault());

        registry
            .Id("employeeId", typeof(EmployeePayProfile))
            .Id("requestId", typeof(PayProfileChangeRequest))
            .NotKey("payGradeId", "a grade belongs to the agency, not to a branch")
            .NotKey("proposedPayGradeId", "a grade belongs to the agency, not to a branch");
    }

    private static OrgScopeKeyRegistry Build()
    {
        var registry = new OrgScopeKeyRegistry(Service);
        Configure(registry);
        return registry;
    }
}
