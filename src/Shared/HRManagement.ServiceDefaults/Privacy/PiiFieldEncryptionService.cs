using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Privacy;

/// <summary>Field-level encryption for PII columns at rest (PRV-001).</summary>
public interface IPiiFieldEncryptionService
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
    bool IsProtected(string value);
}

public sealed class PiiFieldEncryptionService(IDataProtectionProvider provider) : IPiiFieldEncryptionService
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
        catch
        {
            // Legacy plaintext row before encryption rollout.
            return protectedText;
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
