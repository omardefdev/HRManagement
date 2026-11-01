using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults;

/// <summary>One refused key. <see cref="Key"/> is a configuration key name; values are never kept.</summary>
public sealed record ProductionConfigurationFailure(string Key, string ReasonAr, string ReasonEn)
{
    public override string ToString() => $"{Key}: {ReasonAr} / {ReasonEn}";
}

public sealed record ProductionConfigurationReport(
    IReadOnlyList<ProductionConfigurationFailure> Failures,
    IReadOnlyList<string> PlaceholderWarnings)
{
    public bool IsValid => Failures.Count == 0;
}

/// <summary>
/// PLAN-36 (G-OPS-01 blocker 3): refuses to start a service in Production while a critical key still holds a
/// placeholder (<c>YOUR_*</c>, <c>USE_ENV_*</c>), the caller-proof secret is short, the public authority points to
/// localhost, a gateway module entry is empty, the organization name is garbled, or demo mode runs without a seed
/// profile. It reads only the resolved <see cref="IConfiguration"/>, so environment overrides win, and it names the
/// key in Arabic and English without ever printing a value. Placeholders in other keys are logged as warnings.
/// <para>
/// Always on in Production; elsewhere (Development, Acceptance) only with <c>Deployment:EnforceConfigGuard=true</c>.
/// </para>
/// </summary>
public sealed class ProductionConfigurationGuard(
    IConfiguration configuration,
    IHostEnvironment environment,
    IOptions<ProductionConfigurationGuardOptions> options,
    ILogger<ProductionConfigurationGuard> logger) : IHostedService
{
    // UTF-8 Arabic (or any non-Latin text) decoded as Latin-1 / Windows-1252: a lead byte shown as Ã, Ø or Ù followed
    // by a continuation byte (0x80-0xBF) or the Windows-1252 glyph of one.
    private static readonly Regex Mojibake = new(
        "[ÃØÙ][\u0080-¿ŒœŠšŸŽžƒˆ˜–—‘-„†-•…‰‹›€™]",
        RegexOptions.CultureInvariant);

    public static bool IsEnforced(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsProduction()
        || configuration.GetValue(ProductionConfigurationGuardOptions.EnforceKey, false);

    public static bool IsPlaceholder(string? value) =>
        !string.IsNullOrEmpty(value)
        && ProductionConfigurationGuardOptions.PlaceholderMarkers.Any(
            marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsEnforced(configuration, environment))
            return Task.CompletedTask;

        var report = Evaluate(configuration, environment, options.Value);
        foreach (var key in report.PlaceholderWarnings)
        {
            logger.LogWarning(
                "Production configuration guard: {Key} holds a placeholder value (not critical) / المفتاح {Key} يحمل قيمة وهمية (غير حرج)",
                key,
                key);
        }

        if (report.IsValid)
            return Task.CompletedTask;

        foreach (var failure in report.Failures)
        {
            logger.LogCritical(
                "Production configuration guard: {Key} {ReasonEn} / المفتاح {Key} {ReasonAr}",
                failure.Key,
                failure.ReasonEn,
                failure.Key,
                failure.ReasonAr);
        }

        throw new InvalidOperationException(
            "إعداد الإنتاج غير صالح، لن تبدأ الخدمة. Production configuration is invalid, the service will not start: "
            + string.Join("; ", report.Failures));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static ProductionConfigurationReport Evaluate(
        IConfiguration configuration,
        IHostEnvironment environment,
        ProductionConfigurationGuardOptions options)
    {
        var failures = new List<ProductionConfigurationFailure>();
        void Fail(string key, string ar, string en)
        {
            if (!failures.Any(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase) && f.ReasonEn == en))
                failures.Add(new ProductionConfigurationFailure(key, ar, en));
        }

        var critical = options.CriticalKeys.Concat(options.RequiredKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var key in critical)
        {
            var value = configuration[key];
            if (IsPlaceholder(value))
                Fail(key, "يحمل قيمة وهمية", "holds a placeholder value");
        }

        foreach (var key in options.RequiredKeys)
        {
            if (string.IsNullOrWhiteSpace(configuration[key]))
                Fail(key, "غير مضبوط", "is not set");
        }

        var secret = configuration["GatewayCallerProof:Secret"];
        if (!string.IsNullOrWhiteSpace(secret)
            && !IsPlaceholder(secret)
            && secret.Trim().Length < ProductionConfigurationGuardOptions.MinimumCallerProofSecretLength)
        {
            Fail(
                "GatewayCallerProof:Secret",
                $"أقصر من {ProductionConfigurationGuardOptions.MinimumCallerProofSecretLength} حرفاً",
                $"is shorter than {ProductionConfigurationGuardOptions.MinimumCallerProofSecretLength} characters");
        }

        EvaluateCredentials(configuration, environment, Fail);

        var publicAuthority = configuration["Keycloak:PublicAuthority"];
        if (environment.IsProduction()
            && !string.IsNullOrWhiteSpace(publicAuthority)
            && (publicAuthority.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                || publicAuthority.Contains("127.0.0.1", StringComparison.Ordinal)))
        {
            Fail("Keycloak:PublicAuthority", "يشير إلى localhost في الإنتاج", "points to localhost in Production");
        }

        foreach (var module in configuration.GetSection("Gateway:EnabledModules").GetChildren())
        {
            if (string.IsNullOrWhiteSpace(module.Value) && !module.GetChildren().Any())
                Fail("Gateway:EnabledModules", "فيه عنصر فارغ", "has an empty entry");
        }

        foreach (var key in new[] { "Organization:Name", "Organization:ShortName" })
        {
            var value = configuration[key];
            if (!string.IsNullOrEmpty(value) && Mojibake.IsMatch(value))
                Fail(key, "ترميزه مشوّه", "has garbled (mojibake) encoding");
        }

        if (options.ObjectStorageCredentialsRequired
            && string.Equals(configuration["FileStorage:Provider"] ?? "Minio", "Minio", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var key in new[] { "FileStorage:AccessKey", "FileStorage:SecretKey" })
            {
                var value = configuration[key];
                if (string.IsNullOrWhiteSpace(value) || IsPlaceholder(value))
                    Fail(key, "غير مضبوط أو وهمي والمزوّد Minio", "is not set or a placeholder while the provider is Minio");
            }
        }

        if (string.Equals(configuration[ProductionConfigurationGuardOptions.DeploymentModeKey], "demo", StringComparison.OrdinalIgnoreCase))
        {
            SeedProfile? profile;
            try
            {
                profile = SeedProfileResolver.Resolve(configuration, environment);
            }
            catch (InvalidOperationException)
            {
                profile = null;
                Fail("Seed:Profile", "قيمة غير معروفة", "has an unsupported value");
            }

            if (profile == SeedProfile.None)
                Fail("Seed:Profile", "None في وضع العرض (Deployment:Mode=demo)", "is None in demo mode (Deployment:Mode=demo)");
        }

        var failedKeys = failures.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var warnings = configuration.AsEnumerable()
            .Where(pair => pair.Value is not null
                           && IsPlaceholder(pair.Value)
                           && !failedKeys.Contains(pair.Key)
                           && !critical.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        return new ProductionConfigurationReport(failures, warnings);
    }

    /// <summary>
    /// PLAN-38 (section 3): the credentials a service connects with. Every configured connection string, the broker user
    /// and password (when the service has a <c>RabbitMQ</c> section), and the service-identity secret (when a client id
    /// is set) must be present and real; the authority must not point to localhost in Production; and the two postures
    /// of the single host (D-38-1, D-38-2) are enforced: <c>Deployment:MqPosture=least-privilege</c> refuses any broker
    /// user but a <c>mq_*</c> service user, <c>Deployment:DbPosture=least-privilege</c> refuses any database user but a
    /// <c>*_app</c> role, and <c>Deployment:DbPosture=demo</c> exists only in demo mode. No value is ever reported.
    /// </summary>
    private static void EvaluateCredentials(
        IConfiguration configuration,
        IHostEnvironment environment,
        Action<string, string, string> fail)
    {
        void RequireReal(string key)
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value))
                fail(key, "غير مضبوط", "is not set");
            else if (IsPlaceholder(value))
                fail(key, "يحمل قيمة وهمية", "holds a placeholder value");
        }

        var connectionStrings = configuration.GetSection("ConnectionStrings").GetChildren().ToList();
        foreach (var connectionString in connectionStrings)
            RequireReal(connectionString.Path);

        var rabbit = configuration.GetSection("RabbitMQ");
        if (rabbit.Exists())
        {
            RequireReal("RabbitMQ:Username");
            RequireReal("RabbitMQ:Password");
        }

        if (!string.IsNullOrWhiteSpace(configuration["ServiceIdentity:ClientId"]))
            RequireReal("ServiceIdentity:ClientSecret");

        var authority = configuration["Keycloak:Authority"];
        if (environment.IsProduction()
            && !string.IsNullOrWhiteSpace(authority)
            && (authority.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                || authority.Contains("127.0.0.1", StringComparison.Ordinal)))
        {
            fail("Keycloak:Authority", "يشير إلى localhost في الإنتاج", "points to localhost in Production");
        }

        var mqPosture = configuration[ProductionConfigurationGuardOptions.MqPostureKey];
        if (!string.IsNullOrWhiteSpace(mqPosture))
        {
            if (!ProductionConfigurationGuardOptions.MqPostures.Contains(mqPosture, StringComparer.OrdinalIgnoreCase))
            {
                fail(ProductionConfigurationGuardOptions.MqPostureKey, "قيمة غير معروفة", "has an unsupported value");
            }
            else if (string.Equals(mqPosture, "least-privilege", StringComparison.OrdinalIgnoreCase)
                     && configuration["RabbitMQ:Username"] is { Length: > 0 } user
                     && !IsPlaceholder(user)
                     && !user.StartsWith("mq_", StringComparison.Ordinal))
            {
                fail(
                    "RabbitMQ:Username",
                    "ليس مستخدم خدمة mq_* والوضع least-privilege (الحساب الإداري مرفوض)",
                    "is not a mq_* service user while Deployment:MqPosture=least-privilege (the administrator is refused)");
            }
        }

        var dbPosture = configuration[ProductionConfigurationGuardOptions.DbPostureKey];
        if (!string.IsNullOrWhiteSpace(dbPosture))
        {
            if (!ProductionConfigurationGuardOptions.DbPostures.Contains(dbPosture, StringComparer.OrdinalIgnoreCase))
            {
                fail(ProductionConfigurationGuardOptions.DbPostureKey, "قيمة غير معروفة", "has an unsupported value");
            }
            else if (string.Equals(dbPosture, "demo", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(configuration[ProductionConfigurationGuardOptions.DeploymentModeKey], "demo", StringComparison.OrdinalIgnoreCase))
            {
                fail(ProductionConfigurationGuardOptions.DbPostureKey, "demo خارج وضع العرض", "is demo outside demo mode (Deployment:Mode=demo)");
            }
            else if (string.Equals(dbPosture, "least-privilege", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var connectionString in connectionStrings)
                {
                    if (string.IsNullOrWhiteSpace(connectionString.Value) || IsPlaceholder(connectionString.Value))
                        continue;
                    string? user;
                    try
                    {
                        user = new Npgsql.NpgsqlConnectionStringBuilder(connectionString.Value).Username;
                    }
                    catch (ArgumentException)
                    {
                        fail(connectionString.Path, "صيغته غير صالحة", "is not a valid connection string");
                        continue;
                    }

                    if (user is null || !user.EndsWith("_app", StringComparison.Ordinal))
                    {
                        fail(
                            connectionString.Path,
                            "لا يتصل بدور *_app والوضع least-privilege",
                            "does not connect as a *_app role while Deployment:DbPosture=least-privilege");
                    }
                }
            }
        }
    }
}
