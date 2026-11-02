using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>What <see cref="DataProtectionExtensions.AddHrDataProtection"/> registered (names and paths, no key material).</summary>
public sealed record HrDataProtectionRegistration(string ApplicationName, string? KeyRingPath, string? CertificatePath);

public static class DataProtectionExtensions
{
    public const string KeyRingPathKey = "DataProtection:KeyRingPath";
    public const string CertificatePathKey = "DataProtection:CertificatePath";
    public const string CertificatePasswordKey = "DataProtection:CertificatePassword";
    public const string PreviousCertificatePathsKey = "DataProtection:PreviousCertificatePaths";
    public const string RequirePersistentKeyRingKey = "DataProtection:RequirePersistentKeyRing";
    public const string AllowKeyRingResetKey = "DataProtection:AllowKeyRingReset";

    /// <summary>
    /// Persists the Data Protection key ring to shared storage so every replica can decrypt what
    /// the others encrypted.
    /// <para>
    /// With the framework default each instance generates its own in-memory key ring. Behind more
    /// than one gateway replica that breaks interactive sign-in outright: the OIDC correlation and
    /// nonce cookies written by the replica that started the login cannot be read by whichever
    /// replica receives the callback, and auth cookies stop working on every rollout.
    /// </para>
    /// <para>
    /// <c>SetApplicationName</c> is equally load-bearing — the name is part of key derivation, so
    /// replicas must agree on it to share a ring at all.
    /// </para>
    /// <para>
    /// PLAN-39 (D-39-3): with <c>DataProtection:CertificatePath</c> (a PFX; its password in
    /// <c>DataProtection:CertificatePassword</c>, from the environment) new keys are encrypted at rest with that
    /// certificate, and keys encrypted with it or with any certificate of <c>DataProtection:PreviousCertificatePaths</c>
    /// (rotation) are read; keys written before the certificate (unencrypted) stay readable. Off on the demo server.
    /// <see cref="DataProtectionStartupGuard"/> refuses a Production start without a persistent, writable key ring.
    /// </para>
    /// </summary>
    public static WebApplicationBuilder AddHrDataProtection(
        this WebApplicationBuilder builder,
        string applicationName)
    {
        var keyRingPath = builder.Configuration[KeyRingPathKey];
        var certificatePath = builder.Configuration[CertificatePathKey];

        var dataProtection = builder.Services
            .AddDataProtection()
            // Shared across replicas of the same app, and stable across restarts.
            .SetApplicationName(applicationName);

        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            var directory = new DirectoryInfo(keyRingPath);
            try
            {
                directory.Create();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The start-up guard reports an unusable path with the service name; registration must not hide it.
            }

            dataProtection.PersistKeysToFileSystem(directory);
        }

        // A configured but missing file is reported by DataProtectionStartupGuard with the service name, not thrown here.
        if (!string.IsNullOrWhiteSpace(certificatePath) && File.Exists(certificatePath))
        {
            var password = builder.Configuration[CertificatePasswordKey];
            var current = LoadCertificate(certificatePath, password);
            var previous = ReadPaths(builder.Configuration, PreviousCertificatePathsKey)
                .Select(path => LoadCertificate(path, password))
                .ToArray();
            dataProtection.ProtectKeysWithCertificate(current);
            dataProtection.UnprotectKeysWithAnyCertificate([current, .. previous]);
        }

        builder.Services.AddSingleton(new HrDataProtectionRegistration(applicationName, keyRingPath, certificatePath));
        builder.Services.AddHrStartupCheck<DataProtectionStartupGuard>();
        return builder;
    }

    /// <summary>A PFX with its private key; ephemeral on Linux so no key is written to a user store in the container.</summary>
    public static X509Certificate2 LoadCertificate(string path, string? password)
    {
        var flags = OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        return X509CertificateLoader.LoadPkcs12FromFile(path, password, flags);
    }

    /// <summary>A configuration list given as children (<c>Key:0</c>, <c>Key:1</c>) or as one value separated by ';'.</summary>
    public static IReadOnlyList<string> ReadPaths(IConfiguration configuration, string key)
    {
        var children = configuration.GetSection(key).GetChildren().Select(c => c.Value).ToList();
        var values = children.Count > 0 ? children : [configuration[key]];
        return values
            .SelectMany(v => (v ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
    }
}
