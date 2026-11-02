using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-39 (section 4, D-39-3, P-3): in Production, or with <c>DataProtection:RequirePersistentKeyRing=true</c>, a
/// service does not start unless its Data Protection key ring lives on a persistent path it can write (a probe file is
/// written and removed), its application name is set (it is part of key derivation, so a missing or changed name is a
/// lost ring), and a configured protection certificate exists. Without a persistent ring every restart would generate a
/// new one in memory and silently break sign-in cookies, antiforgery tokens and, in HR.Core, the protected national IDs.
/// Runs as an <see cref="IHrStartupCheck"/>, before migrations and seeds. Messages name the service and the path, never
/// a key.
/// </summary>
public sealed class DataProtectionStartupGuard(
    IConfiguration configuration,
    IHostEnvironment environment,
    HrDataProtectionRegistration registration,
    IOptions<DataProtectionOptions> dataProtectionOptions,
    ILogger<DataProtectionStartupGuard> logger) : IHrStartupCheck
{
    public string Name => "data-protection-key-ring";

    public static bool IsEnforced(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsProduction()
        || configuration.GetValue(DataProtectionExtensions.RequirePersistentKeyRingKey, false);

    public Task RunAsync(CancellationToken cancellationToken)
    {
        if (!IsEnforced(configuration, environment))
            return Task.CompletedTask;

        var failures = Evaluate(registration, dataProtectionOptions.Value.ApplicationDiscriminator,
            configuration.GetValue(AllowUnmountedKeyRingKey, false));
        if (failures.Count == 0)
        {
            logger.LogInformation(
                "Data Protection key ring of {Service} persisted at {Path} (certificate protection {Certificate})",
                registration.ApplicationName, registration.KeyRingPath,
                string.IsNullOrWhiteSpace(registration.CertificatePath) ? "off" : "on");
            return Task.CompletedTask;
        }

        foreach (var failure in failures)
            logger.LogCritical("Data Protection start-up guard: {Failure}", failure);
        throw new InvalidOperationException(
            $"حماية البيانات: لن تبدأ الخدمة {registration.ApplicationName}. Data Protection: {registration.ApplicationName} will not start: "
            + string.Join("; ", failures));
    }

    public const string AllowUnmountedKeyRingKey = "DataProtection:AllowUnmountedKeyRing";

    /// <summary>
    /// The mount point holding <paramref name="path"/> (the longest mount point that contains it) from
    /// /proc/self/mountinfo, or null where the mount table cannot be read (not Linux): the check is then skipped.
    /// </summary>
    public static string? MountPointOf(string path)
    {
        const string mountInfo = "/proc/self/mountinfo";
        if (!OperatingSystem.IsLinux() || !File.Exists(mountInfo))
            return null;
        try
        {
            return MountPointOf(File.ReadAllText(mountInfo), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The longest mount point of a mountinfo text (field 5, octal escapes decoded) that contains the path.</summary>
    public static string? MountPointOf(string mountInfo, string path)
    {
        // An absolute POSIX path is taken as it is (also when the test runs on Windows); others are resolved first.
        var full = (path.StartsWith('/') ? path : Path.GetFullPath(path)).TrimEnd('/');
        if (full.Length == 0) full = "/";
        string? best = null;
        foreach (var line in mountInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ');
            if (fields.Length < 5) continue;
            var mount = System.Text.RegularExpressions.Regex.Replace(fields[4], @"\\([0-7]{3})",
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString());
            var contains = mount == "/" || full == mount || full.StartsWith(mount.TrimEnd('/') + "/", StringComparison.Ordinal);
            if (contains && (best is null || mount.Length > best.Length))
                best = mount;
        }
        return best;
    }

    /// <summary>The refusals for one registration (empty when the key ring is usable). Names and paths only.</summary>
    public static IReadOnlyList<string> Evaluate(
        HrDataProtectionRegistration registration,
        string? applicationDiscriminator,
        bool allowUnmountedKeyRing = false,
        Func<string, string?>? mountPointOf = null)
    {
        var failures = new List<string>();
        var service = string.IsNullOrWhiteSpace(registration.ApplicationName) ? "(unnamed service)" : registration.ApplicationName;

        if (string.IsNullOrWhiteSpace(registration.ApplicationName)
            || !string.Equals(applicationDiscriminator, registration.ApplicationName, StringComparison.Ordinal))
        {
            failures.Add($"{service}: اسم التطبيق (SetApplicationName) غير مضبوط / the application name (SetApplicationName) is not set");
        }

        var path = registration.KeyRingPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            failures.Add($"{service}: {DataProtectionExtensions.KeyRingPathKey} غير مضبوط، فستُولَّد حلقة جديدة في الذاكرة عند كل إعادة تشغيل / "
                         + $"{DataProtectionExtensions.KeyRingPathKey} is not set, so every restart would generate a new key ring in memory");
        }
        else
        {
            var probe = Path.Combine(path, $".hrm-dp-write-probe-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(path);
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                failures.Add($"{service}: مسار حلقة المفاتيح {path} غير قابل للكتابة / the key ring path {path} is not writable ({ex.GetType().Name})");
            }

            // Review F2: a writable path is not enough in a container: without its volume the ring lives in the
            // container's own layer and a recreate silently replaces it. The path must be on a file system other than
            // the root one (a volume or bind mount); DataProtection:AllowUnmountedKeyRing=true accepts a host install.
            if (!allowUnmountedKeyRing && (mountPointOf ?? MountPointOf)(path) == "/")
            {
                failures.Add($"{service}: مسار حلقة المفاتيح {path} على نظام ملفات الحاوية لا على volume، فيضيع عند إعادة الإنشاء / "
                             + $"the key ring path {path} is on the container's root file system, not on a volume, so a recreate would lose it "
                             + $"(mount a volume there, or set {AllowUnmountedKeyRingKey}=true on a host install)");
            }
        }

        if (!string.IsNullOrWhiteSpace(registration.CertificatePath) && !File.Exists(registration.CertificatePath))
        {
            failures.Add($"{service}: شهادة حماية المفاتيح {registration.CertificatePath} غير موجودة / "
                         + $"the key protection certificate {registration.CertificatePath} does not exist");
        }

        return failures;
    }
}
