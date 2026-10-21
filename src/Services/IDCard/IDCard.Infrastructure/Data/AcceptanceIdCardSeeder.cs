using HRManagement.ServiceDefaults.Seeding;

namespace IDCard.Infrastructure.Data;

public static class AcceptanceIdCardSeeder
{
    public static Task SeedAsync(IDCardDbContext db, CancellationToken ct = default) =>
        AcceptanceSeedRunner.ForEachAgencyAsync(db, DemoIDCardSeeder.SeedAsync, ct);
}
