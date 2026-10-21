using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace HRManagement.ServiceDefaults.Privacy;

/// <summary>Field-level encryption for PII columns at rest (PRV-001).</summary>
public interface IPiiFieldEncryptionService
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
    bool IsProtected(string value);
}

public sealed class PiiFieldEncryptionService(
    IDataProtectionProvider provider,
    ILogger<PiiFieldEncryptionService> logger) : IPiiFieldEncryptionService
{
    private const string Purpose = "HRManagement.PiiField.v1";
    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
            return protectedText;
        if (!IsProtected(protectedText))
            return protectedText;
        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unable to decrypt a protected PII value. The Data Protection key ring is unavailable or incompatible.");
            throw new CryptographicException(
                "تعذر فك تشفير البيانات المحمية. تحقق من استمرارية مفاتيح حماية البيانات قبل متابعة التشغيل.",
                ex);
        }
    }

    public bool IsProtected(string value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith("CfDJ8", StringComparison.Ordinal);
}

public static class PiiFieldEncryptionServiceCollectionExtensions
{
    public static IServiceCollection AddHrPiiFieldEncryption(this IServiceCollection services)
    {
        services.AddSingleton<IPiiFieldEncryptionService, PiiFieldEncryptionService>();
        return services;
    }
}
