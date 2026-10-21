using HRManagement.ServiceDefaults.Seeding;

namespace IT.Infra.Infrastructure.Data;

public static class AcceptanceITSeeder
{
    public static Task SeedAsync(ITInfraDbContext db, CancellationToken ct = default) =>
        AcceptanceSeedRunner.ForEachAgencyAsync(db, DemoITSeeder.SeedAsync, ct);
}
