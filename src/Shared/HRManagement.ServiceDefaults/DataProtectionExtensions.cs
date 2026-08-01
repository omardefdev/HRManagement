using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public static class DataProtectionExtensions
{
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
    /// </summary>
    public static WebApplicationBuilder AddHrDataProtection(
        this WebApplicationBuilder builder,
        string applicationName)
    {
        var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];

        var dataProtection = builder.Services
            .AddDataProtection()
            // Shared across replicas of the same app, and stable across restarts.
            .SetApplicationName(applicationName);

        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            var directory = new DirectoryInfo(keyRingPath);
            directory.Create();
            dataProtection.PersistKeysToFileSystem(directory);
        }

        return builder;
    }
}
