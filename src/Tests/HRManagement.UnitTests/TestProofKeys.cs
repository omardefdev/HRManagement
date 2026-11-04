using System.Security.Cryptography;

namespace HRManagement.UnitTests;

/// <summary>PLAN-42: gateway proof keys made at run time, so no key, even a test one, is ever in the repository.</summary>
internal static class TestProofKeys
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static string PublicKey { get; } = Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());

    public static string PrivateKey { get; } = Convert.ToBase64String(Key.ExportPkcs8PrivateKey());
}
