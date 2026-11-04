using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

namespace HRManagement.ServiceDefaults;

/// <summary>The outcome of checking a proof. A fixed name, so a log line never repeats what the caller sent.</summary>
public enum GatewayProofResult
{
    Valid,
    Missing,
    Malformed,
    Skew,
    PathMismatch,
    BadSignature,
    Replay,
    NoKey
}

/// <summary>
/// Proof that a request came through the gateway (SEC-001). PLAN-42 (G-IAM-13): v2 is an ECDSA P-256 signature that only the gateway
/// can make. It covers the time, a nonce, the method, the path and a hash of the <c>Authorization</c> header, so a captured proof
/// is worthless with another token and works once. Service accounts need no proof: their token says who they are, and no service
/// holds anything that makes one. The HMAC proof (v1) survives for Development only (D-S2S-2).
/// </summary>
public static class GatewayCallerProof
{
    public const string ProofHeader = "X-HR-Gateway-Proof";
    public const string TimestampHeader = "X-HR-Gateway-Proof-Ts";
    public const string PathHeader = "X-HR-Gateway-Proof-Path";
    public const string NonceHeader = "X-HR-Gateway-Proof-Nonce";

    private const string AuthorizationHeader = "Authorization";
    private const int NonceBytes = 16;
    private static readonly TimeSpan V1MaxSkew = TimeSpan.FromMinutes(5);

    /// <summary>Signs <paramref name="request"/> after its <c>Authorization</c> header is final.</summary>
    public static void SignV2(HttpRequestMessage request, ECDsa privateKey, string pathAndQuery)
    {
        var path = string.IsNullOrWhiteSpace(pathAndQuery) ? "/" : pathAndQuery;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(NonceBytes));
        var authorization = request.Headers.NonValidated.TryGetValues(AuthorizationHeader, out var values)
            ? values.ToString()
            : string.Empty;
        var payload = Payload(ts, nonce, request.Method.Method, path, authorization);
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        foreach (var header in new[] { ProofHeader, TimestampHeader, PathHeader, NonceHeader })
            request.Headers.Remove(header);
        request.Headers.TryAddWithoutValidation(ProofHeader, WebEncoders.Base64UrlEncode(signature));
        request.Headers.TryAddWithoutValidation(TimestampHeader, ts);
        request.Headers.TryAddWithoutValidation(NonceHeader, nonce);
        request.Headers.TryAddWithoutValidation(PathHeader, path);
    }

    public static GatewayProofResult ValidateV2(
        HttpContext context,
        ECDsa? publicKey,
        IGatewayProofReplayCache replayCache,
        TimeSpan maxSkew)
    {
        if (publicKey is null)
            return GatewayProofResult.NoKey;

        var headers = context.Request.Headers;
        var proof = headers[ProofHeader].ToString();
        var tsText = headers[TimestampHeader].ToString();
        var nonce = headers[NonceHeader].ToString();
        var signedPath = headers[PathHeader].ToString();
        if (proof.Length == 0 || tsText.Length == 0 || nonce.Length == 0 || signedPath.Length == 0)
            return GatewayProofResult.Missing;

        if (!long.TryParse(tsText, NumberStyles.None, CultureInfo.InvariantCulture, out var ts)
            || ts > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            return GatewayProofResult.Malformed;

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(ts);
        if (age.Duration() > maxSkew)
            return GatewayProofResult.Skew;

        if (!string.Equals(signedPath, $"{context.Request.Path}{context.Request.QueryString}", StringComparison.Ordinal))
            return GatewayProofResult.PathMismatch;

        byte[] signature;
        try
        {
            signature = WebEncoders.Base64UrlDecode(proof);
        }
        catch (FormatException)
        {
            return GatewayProofResult.Malformed;
        }

        var payload = Payload(tsText, nonce, context.Request.Method, signedPath, headers.Authorization.ToString());
        if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            return GatewayProofResult.BadSignature;

        return replayCache.TryRegister(nonce, maxSkew * 2) ? GatewayProofResult.Valid : GatewayProofResult.Replay;
    }

    private static byte[] Payload(string ts, string nonce, string method, string path, string authorization)
    {
        var tokenHash = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(authorization)));
        return Encoding.UTF8.GetBytes($"v2\n{ts}\n{nonce}\n{method}\n{path}\n{tokenHash}");
    }

    /// <summary>The old shared secret; read in Development only, under <c>AllowV1InDevelopment</c>.</summary>
    public static string? GetV1Secret(IConfiguration configuration) =>
        configuration["GatewayCallerProof:Secret"]
        ?? Environment.GetEnvironmentVariable("GatewayCallerProof__Secret");

    public static void SignV1(HttpRequestMessage request, IConfiguration configuration, string pathAndQuery)
    {
        var secret = GetV1Secret(configuration);
        if (string.IsNullOrWhiteSpace(secret))
            return;

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var effectivePath = string.IsNullOrWhiteSpace(pathAndQuery) ? "/" : pathAndQuery;
        var proof = ComputeHmac(secret, $"{ts}\n{request.Method.Method}\n{effectivePath}");

        request.Headers.Remove(ProofHeader);
        request.Headers.Remove(TimestampHeader);
        request.Headers.Remove(PathHeader);
        request.Headers.TryAddWithoutValidation(ProofHeader, proof);
        request.Headers.TryAddWithoutValidation(TimestampHeader, ts);
        request.Headers.TryAddWithoutValidation(PathHeader, effectivePath);
    }

    public static bool ValidateV1(HttpContext context, IConfiguration configuration)
    {
        var secret = GetV1Secret(configuration);
        if (string.IsNullOrWhiteSpace(secret))
            return false;

        if (!context.Request.Headers.TryGetValue(ProofHeader, out var proofValues)
            || !context.Request.Headers.TryGetValue(TimestampHeader, out var tsValues))
            return false;

        var proof = proofValues.ToString();
        if (string.IsNullOrWhiteSpace(proof) || !long.TryParse(tsValues.ToString(), out var ts))
            return false;

        if ((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(ts)).Duration() > V1MaxSkew)
            return false;

        var actualPath = $"{context.Request.Path}{context.Request.QueryString}";
        var signedPath = actualPath;
        if (context.Request.Headers.TryGetValue(PathHeader, out var pathValues))
        {
            signedPath = pathValues.ToString();
            if (!string.Equals(signedPath, actualPath, StringComparison.Ordinal))
                return false;
        }

        var expected = ComputeHmac(secret, $"{ts}\n{context.Request.Method}\n{signedPath}");
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(proof),
            Encoding.UTF8.GetBytes(expected));
    }

    private static string ComputeHmac(string secret, string payload) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));
}
