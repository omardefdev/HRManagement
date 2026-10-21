using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// HMAC proof that a request was proxied through the gateway (SEC-001 caller proof).
/// Shared secret: configuration <c>GatewayCallerProof:Secret</c> or env <c>GatewayCallerProof__Secret</c>.
/// </summary>
public static class GatewayCallerProof
{
    public const string ProofHeader = "X-HR-Gateway-Proof";
    public const string TimestampHeader = "X-HR-Gateway-Proof-Ts";
    public const string PathHeader = "X-HR-Gateway-Proof-Path";

    private static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

    public static string? GetSecret(IConfiguration configuration) =>
        configuration["GatewayCallerProof:Secret"]
        ?? Environment.GetEnvironmentVariable("GatewayCallerProof__Secret");

    public static void Sign(HttpRequestMessage request, IConfiguration configuration)
    {
        Sign(request, configuration, request.RequestUri?.PathAndQuery ?? "/");
    }

    public static void Sign(
        HttpRequestMessage request,
        IConfiguration configuration,
        string pathAndQuery)
    {
        var secret = GetSecret(configuration);
        if (string.IsNullOrWhiteSpace(secret))
            return;

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var method = request.Method.Method;
        var effectivePath = string.IsNullOrWhiteSpace(pathAndQuery) ? "/" : pathAndQuery;
        var payload = $"{ts}\n{method}\n{effectivePath}";
        var proof = ComputeHmac(secret, payload);

        request.Headers.Remove(ProofHeader);
        request.Headers.Remove(TimestampHeader);
        request.Headers.Remove(PathHeader);
        request.Headers.TryAddWithoutValidation(ProofHeader, proof);
        request.Headers.TryAddWithoutValidation(TimestampHeader, ts);
        request.Headers.TryAddWithoutValidation(PathHeader, effectivePath);
    }

    public static bool Validate(HttpContext context, IConfiguration configuration, bool enforce)
    {
        var secret = GetSecret(configuration);
        if (string.IsNullOrWhiteSpace(secret))
            return !enforce;

        if (!context.Request.Headers.TryGetValue(ProofHeader, out var proofValues)
            || !context.Request.Headers.TryGetValue(TimestampHeader, out var tsValues))
            return false;

        var proof = proofValues.ToString();
        var tsText = tsValues.ToString();
        if (string.IsNullOrWhiteSpace(proof) || !long.TryParse(tsText, out var ts))
            return false;

        var requestTs = DateTimeOffset.FromUnixTimeSeconds(ts);
        if (Math.Abs((DateTimeOffset.UtcNow - requestTs).TotalMinutes) > MaxSkew.TotalMinutes)
            return false;

        var actualPath = $"{context.Request.Path}{context.Request.QueryString}";
        var signedPath = actualPath;
        if (context.Request.Headers.TryGetValue(PathHeader, out var pathValues))
        {
            signedPath = pathValues.ToString();
            if (!string.Equals(signedPath, actualPath, StringComparison.Ordinal))
                return false;
        }

        var payload = $"{ts}\n{context.Request.Method}\n{signedPath}";
        var expected = ComputeHmac(secret, payload);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(proof),
            Encoding.UTF8.GetBytes(expected));
    }

    private static string ComputeHmac(string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToBase64String(hash);
    }
}
