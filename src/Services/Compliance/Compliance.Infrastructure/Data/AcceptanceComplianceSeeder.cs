using HRManagement.ServiceDefaults.Seeding;

namespace Compliance.Infrastructure.Data;

public static class AcceptanceComplianceSeeder
{
    public static Task SeedAsync(ComplianceDbContext db, CancellationToken ct = default) =>
        AcceptanceSeedRunner.ForEachAgencyAsync(db, DemoComplianceSeeder.SeedAsync, ct);
}
