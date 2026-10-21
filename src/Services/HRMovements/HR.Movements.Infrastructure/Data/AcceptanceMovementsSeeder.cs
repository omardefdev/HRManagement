using HRManagement.ServiceDefaults.Seeding;

namespace HR.Movements.Infrastructure.Data;

public static class AcceptanceMovementsSeeder
{
    public static Task SeedAsync(MovementsDbContext db, CancellationToken ct = default) =>
        AcceptanceSeedRunner.ForEachAgencyAsync(db, DemoMovementsSeeder.SeedAsync, ct);
}
