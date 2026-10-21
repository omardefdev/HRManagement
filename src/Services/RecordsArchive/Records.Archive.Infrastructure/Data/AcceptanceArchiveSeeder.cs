using HRManagement.ServiceDefaults.Seeding;

namespace Records.Archive.Infrastructure.Data;

public static class AcceptanceArchiveSeeder
{
    public static Task SeedAsync(ArchiveDbContext db, CancellationToken ct = default) =>
        AcceptanceSeedRunner.ForEachAgencyAsync(db, DemoArchiveSeeder.SeedAsync, ct);
}
