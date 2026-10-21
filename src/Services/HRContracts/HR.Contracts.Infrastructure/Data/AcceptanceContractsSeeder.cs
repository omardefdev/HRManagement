using HRManagement.ServiceDefaults.Seeding;

namespace HR.Contracts.Infrastructure.Data;

public static class AcceptanceContractsSeeder
{
    public static Task SeedAsync(ContractsDbContext db, CancellationToken ct = default) =>
        AcceptanceSeedRunner.ForEachAgencyAsync(db, DemoContractsSeeder.SeedAsync, ct);
}
