using HRManagement.ServiceDefaults.Seeding;

namespace HR.Training.Infrastructure.Data;

public static class AcceptanceTrainingSeeder
{
    public static Task SeedAsync(TrainingDbContext db, CancellationToken ct = default) =>
        AcceptanceSeedRunner.ForEachAgencyAsync(
            db,
            async (context, token) =>
            {
                await ELearningSeeder.SeedAsync(context, token);
                await DemoTrainingSeeder.SeedAsync(context, token);
            },
            ct);
}
