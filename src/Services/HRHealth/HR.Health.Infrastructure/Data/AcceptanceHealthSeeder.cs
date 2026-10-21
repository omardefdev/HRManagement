using HRManagement.ServiceDefaults.Seeding;

namespace HR.Health.Infrastructure.Data;

public static class AcceptanceHealthSeeder
{
    public static Task SeedAsync(HealthDbContext db, CancellationToken ct = default) =>
        AcceptanceSeedRunner.ForEachAgencyAsync(db, DemoHealthSeeder.SeedAsync, ct);
}
