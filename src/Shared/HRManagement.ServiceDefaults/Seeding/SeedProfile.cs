using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HRManagement.ServiceDefaults.Seeding;

public enum SeedProfile
{
    None = 0,
    Demo = 1,
    Acceptance = 2
}

public static class SeedProfileResolver
{
    public static SeedProfile Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Seed:Profile"]
            ?? Environment.GetEnvironmentVariable("SEED_PROFILE");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Enum.TryParse<SeedProfile>(configured, ignoreCase: true, out var parsed))
                return parsed;

            throw new InvalidOperationException(
                $"Unsupported Seed:Profile '{configured}'. Expected None, Demo, or Acceptance.");
        }

        // Compatibility while local scripts and developer machines move to Seed:Profile.
        var legacyAcceptance = configuration.GetValue("Seed:Acceptance", false)
            || string.Equals(Environment.GetEnvironmentVariable("SEED_ACCEPTANCE"), "true", StringComparison.OrdinalIgnoreCase);
        if (legacyAcceptance)
            return SeedProfile.Acceptance;

        return environment.IsDevelopment() ? SeedProfile.Demo : SeedProfile.None;
    }

    public static bool Is(this SeedProfile profile, SeedProfile expected) => profile == expected;
}
