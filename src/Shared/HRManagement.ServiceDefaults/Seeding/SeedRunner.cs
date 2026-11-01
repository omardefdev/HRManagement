using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HRManagement.ServiceDefaults.Seeding;

/// <summary>A mandatory seed failed; the service does not start (PLAN-38, D-38-6). Carries no data.</summary>
public sealed class MandatorySeedFailedException(string service, string seed, string cause)
    : InvalidOperationException(
        $"Mandatory seed '{seed}' of {service} failed ({cause}); the service will not start. "
        + $"البذرة الإلزامية '{seed}' في {service} فشلت ({cause})، لن تبدأ الخدمة. "
        + $"Emergency only: {SeedRunner.AllowMandatoryFailureKey}=true.")
{
    public string Service { get; } = service;
    public string Seed { get; } = seed;
}

/// <summary>
/// PLAN-38 (section 4, D-38-6): one start-up rule for seeds, instead of a warning that every service wrote its own way.
/// <list type="bullet">
/// <item><see cref="RunMandatoryAsync"/>: national and reference data the service cannot work without. A failure stops the
/// start in Production (and in the Acceptance profile, as before); <c>Seed:AllowMandatoryFailure=true</c> is the emergency
/// switch: the failure is logged as Critical and counted (<c>seed_mandatory_failures_total</c>) and the start goes on.
/// Development logs a warning.</item>
/// <item><see cref="RunOptionalAsync"/>: demo and acceptance data. A failure is a warning, except in the Acceptance
/// profile where it still fails the start.</item>
/// </list>
/// Messages name the seed and the service and the cause as an exception type and SQL state only: an exception message
/// can quote row values, so it is never logged or rethrown.
/// </summary>
public sealed class SeedRunner
{
    public const string AllowMandatoryFailureKey = "Seed:AllowMandatoryFailure";

    private static readonly Meter Meter = new("HRManagement.Seeding");
    private static readonly Counter<long> MandatoryFailures =
        Meter.CreateCounter<long>("seed_mandatory_failures_total", description: "Mandatory seeds that failed at start-up");

    private readonly string _service;
    private readonly SeedProfile _profile;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;

    public SeedRunner(string service, SeedProfile profile, IHostEnvironment environment, IConfiguration configuration, ILogger logger)
    {
        _service = service;
        _profile = profile;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RunMandatoryAsync(string seed, Func<Task> run)
    {
        try
        {
            await run();
        }
        catch (Exception ex)
        {
            // The Acceptance profile keeps its behaviour: the original exception fails the start (synthetic lab data).
            if (_profile == SeedProfile.Acceptance)
                throw;

            var cause = Describe(ex);

            if (_environment.IsProduction())
            {
                MandatoryFailures.Add(1, new KeyValuePair<string, object?>("service", _service), new KeyValuePair<string, object?>("seed", seed));
                if (_configuration.GetValue(AllowMandatoryFailureKey, false))
                {
                    _logger.LogCritical(
                        "Mandatory seed {Seed} of {Service} failed ({Cause}); starting anyway because {Switch}=true (emergency switch) / البذرة الإلزامية {Seed} فشلت والخدمة تبدأ بمفتاح الطوارئ",
                        seed, _service, cause, AllowMandatoryFailureKey, seed);
                    return;
                }

                _logger.LogCritical(
                    "Mandatory seed {Seed} of {Service} failed ({Cause}); the service will not start / البذرة الإلزامية {Seed} فشلت، لن تبدأ الخدمة",
                    seed, _service, cause, seed);
                throw new MandatorySeedFailedException(_service, seed, cause);
            }

            _logger.LogWarning(
                "Mandatory seed {Seed} of {Service} failed ({Cause}); continuing outside Production",
                seed, _service, cause);
        }
    }

    public async Task RunOptionalAsync(string seed, Func<Task> run)
    {
        try
        {
            await run();
        }
        catch (Exception ex) when (_profile != SeedProfile.Acceptance)
        {
            _logger.LogWarning("Optional seed {Seed} of {Service} skipped ({Cause})", seed, _service, Describe(ex));
        }
    }

    /// <summary>The exception type and, for PostgreSQL, the SQL state; never the message.</summary>
    public static string Describe(Exception ex)
    {
        var root = ex;
        while (root is not PostgresException && root.InnerException is not null)
            root = root.InnerException;
        return root is PostgresException pg ? $"{ex.GetType().Name}, SQLSTATE {pg.SqlState}" : ex.GetType().Name;
    }
}
