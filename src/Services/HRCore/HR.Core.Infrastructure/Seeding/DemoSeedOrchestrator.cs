using HR.Core.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

public static class DemoSeedOrchestrator
{
    public static async Task ApplyAsync(HRCoreDbContext db, IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("DemoSeed");
        logger?.LogInformation("Applying HR.Core demo seed");
        await DemoDataSeeder.SeedAsync(db, logger, ct);
    }
}
