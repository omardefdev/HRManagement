using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Benefits;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

/// <summary>SYR-003/HCM-004: minimal benefit plans for acceptance and demo.</summary>
public static class BenefitPlanSeeder
{
    private static readonly (string PlanCode, string PlanName, BenefitPlanType PlanType, string LegalSourceId, string LegalRegimeId)[]
        DemoPlans =
        [
            ("PENSION-LAW50", "تقاعد — قانون 50/2004", BenefitPlanType.Pension, Syr001LegislationIds.Law50_2004, "law50"),
            ("PENSION-LAW53", "تقاعد — قانون 53/2021", BenefitPlanType.Pension, Syr001LegislationIds.Law53_2021, "law53")
        ];

    public static async Task SeedAsync(HRCoreDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        var effectiveFrom = new DateTime(2004, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = DateTime.UtcNow;

        foreach (var plan in DemoPlans)
        {
            var existing = await db.BenefitPlans
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.PlanCode == plan.PlanCode, ct);

            if (existing is null)
            {
                db.BenefitPlans.Add(new BenefitPlan
                {
                    TenantId = tenantId,
                    PlanCode = plan.PlanCode,
                    PlanName = plan.PlanName,
                    PlanType = plan.PlanType,
                    LegalSourceId = plan.LegalSourceId,
                    LegalRegimeId = plan.LegalRegimeId,
                    EmployeeContributionRate = 0.07m,
                    EmployerContributionRate = 0.11m,
                    EligibilityMinServiceMonths = 0,
                    EffectiveFrom = plan.LegalRegimeId == "law53"
                        ? new DateTime(2021, 8, 1, 0, 0, 0, DateTimeKind.Utc)
                        : effectiveFrom,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                existing.PlanName = plan.PlanName;
                existing.PlanType = plan.PlanType;
                existing.LegalSourceId = plan.LegalSourceId;
                existing.LegalRegimeId = plan.LegalRegimeId;
                existing.IsActive = true;
                existing.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public static Task SeedDefaultTenantAsync(HRCoreDbContext db, CancellationToken ct = default) =>
        SeedAsync(db, TenantDefaults.DefaultTenantId, ct);
}
