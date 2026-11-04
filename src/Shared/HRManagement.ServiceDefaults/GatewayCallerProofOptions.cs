using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-42 (D-S2S-1, D-S2S-2): the gateway proof. <see cref="PrivateKeyPem"/> is set on the gateway alone and
/// <see cref="PublicKeyPem"/> on the modules. Both take PEM or a single-line base64 of the DER (PKCS#8 private, SPKI public),
/// because <c>.env</c> and Compose do not carry multi-line values safely.
/// </summary>
public sealed class GatewayCallerProofOptions
{
    public const string SectionName = "GatewayCallerProof";
    public const string PrivateKeyKey = SectionName + ":PrivateKeyPem";
    public const string PublicKeyKey = SectionName + ":PublicKeyPem";

    public string? PrivateKeyPem { get; set; }

    public string? PublicKeyPem { get; set; }

    public int MaxSkewSeconds { get; set; } = 60;

    /// <summary>Accepts the old HMAC proof in Development only (D-S2S-2). Ignored in every other environment.</summary>
    public bool AllowV1InDevelopment { get; set; }
}

/// <summary>PLAN-42: reads a proof key in either format and accepts ECDSA P-256 only. A failure names no key material.</summary>
public static class GatewayProofKeyLoader
{
    private const string P256Oid = "1.2.840.10045.3.1.7";

    public static ECDsa LoadPrivate(string text) => Load(text, (key, der) => key.ImportPkcs8PrivateKey(der, out _));

    public static ECDsa LoadPublic(string text) => Load(text, (key, der) => key.ImportSubjectPublicKeyInfo(der, out _));

    public static bool IsValidPrivate(string text) => IsValid(text, LoadPrivate);

    public static bool IsValidPublic(string text) => IsValid(text, LoadPublic);

    private static bool IsValid(string text, Func<string, ECDsa> load)
    {
        try
        {
            using var key = load(text);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static ECDsa Load(string text, Action<ECDsa, byte[]> importDer)
    {
        var key = ECDsa.Create();
        try
        {
            var trimmed = text.Trim();
            if (trimmed.StartsWith("-----BEGIN", StringComparison.Ordinal))
                key.ImportFromPem(trimmed);
            else
                importDer(key, Convert.FromBase64String(trimmed));

            if (key.ExportParameters(false).Curve.Oid?.Value != P256Oid)
                throw new CryptographicException("The gateway proof key is not on the P-256 curve.");
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}

/// <summary>The configured proof keys, loaded once. A host holds at most what its role needs.</summary>
public sealed class GatewayProofKeys(IOptions<GatewayCallerProofOptions> options) : IDisposable
{
    private readonly Lazy<ECDsa?> _private = new(() => Load(options.Value.PrivateKeyPem, GatewayProofKeyLoader.LoadPrivate));
    private readonly Lazy<ECDsa?> _public = new(() => Load(options.Value.PublicKeyPem, GatewayProofKeyLoader.LoadPublic));

    public ECDsa? PrivateKey => _private.Value;

    public ECDsa? PublicKey => _public.Value;

    public TimeSpan MaxSkew => TimeSpan.FromSeconds(options.Value.MaxSkewSeconds);

    public bool AllowV1InDevelopment => options.Value.AllowV1InDevelopment;

    private static ECDsa? Load(string? text, Func<string, ECDsa> load) =>
        string.IsNullOrWhiteSpace(text) ? null : load(text);

    public void Dispose()
    {
        if (_private.IsValueCreated)
            _private.Value?.Dispose();
        if (_public.IsValueCreated)
            _public.Value?.Dispose();
    }
}
