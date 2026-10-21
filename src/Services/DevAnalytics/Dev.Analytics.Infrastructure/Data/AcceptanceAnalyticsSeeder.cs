using HRManagement.ServiceDefaults.Seeding;

namespace Dev.Analytics.Infrastructure.Data;

public static class AcceptanceAnalyticsSeeder
{
    public static Task SeedAsync(AnalyticsDbContext db, CancellationToken ct = default) =>
        AcceptanceSeedRunner.ForEachAgencyAsync(db, DemoAnalyticsSeeder.SeedAsync, ct);
}
