using System.Security.Cryptography;

// PLAN-42 (P-2): the fallback of scripts/s2s042-new-gateway-proof-keys.ps1 when openssl is not on the PATH. Windows PowerShell 5.1
// runs on .NET Framework, which cannot export PKCS#8 or SPKI. No network; it writes the two DER files and prints nothing.
if (args.Length != 2)
{
    Console.Error.WriteLine("usage: S2sKeyGen <private.der> <public.der>");
    return 2;
}

using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
File.WriteAllBytes(args[0], key.ExportPkcs8PrivateKey());
File.WriteAllBytes(args[1], key.ExportSubjectPublicKeyInfo());
return 0;
