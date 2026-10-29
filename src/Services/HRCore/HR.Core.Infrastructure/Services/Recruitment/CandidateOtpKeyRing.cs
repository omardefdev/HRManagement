using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-030 (PLAN-30 H1/H2): the secret behind <see cref="HR.Core.Domain.Entities.CandidateOtpChallenge.CodeHmac"/> and the
/// hashed request address. The key comes from <c>Recruitment:Portal:OtpHmacKey</c> (env <c>Recruitment__Portal__OtpHmacKey</c>,
/// at least 32 characters) and is never stored in the database. Production refuses to start without it
/// (<see cref="CandidateOtpStartupValidator"/>); elsewhere a random per-process key is used (Data Protection has no
/// deterministic key derivation), so codes still pending at a restart stop working — they live 10 minutes.
/// </summary>
public sealed class CandidateOtpKeyRing
{
    public const string ConfigurationKey = "Recruitment:Portal:OtpHmacKey";
    public const int MinKeyLength = 32;

    private const int HmacLength = 32;
    private readonly byte[] key;

    public CandidateOtpKeyRing(IConfiguration configuration, ILogger<CandidateOtpKeyRing> logger)
    {
        var configured = Read(configuration);
        if (configured is not null)
        {
            key = Encoding.UTF8.GetBytes(configured);
            IsConfigured = true;
            return;
        }

        key = RandomNumberGenerator.GetBytes(HmacLength);
        logger.LogWarning(
            "HCM-030 {Key} is not set: using a random per-process key (codes do not survive a restart or reach another replica).",
            ConfigurationKey);
    }

    /// <summary>Fixed key (tests).</summary>
    public CandidateOtpKeyRing(byte[] key)
    {
        if (key.Length < MinKeyLength)
            throw new ArgumentException($"The OTP key must be at least {MinKeyLength} bytes.", nameof(key));
        this.key = key.ToArray();
        IsConfigured = true;
    }

    /// <summary>True when the key came from configuration (false: random per-process fallback).</summary>
    public bool IsConfigured { get; }

    /// <summary>The configured key, or null when missing or shorter than <see cref="MinKeyLength"/>.</summary>
    public static string? Read(IConfiguration configuration)
    {
        var value = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length < MinKeyLength ? null : value;
    }

    /// <summary>Lowercase hex HMAC-SHA256 of <c>{challengeId:D}:{code}</c>.</summary>
    public string CodeHmacHex(Guid challengeId, string code) => Convert.ToHexStringLower(CodeHmac(challengeId, code));

    /// <summary>
    /// Constant-time check of <paramref name="code"/> against a stored <see cref="CodeHmacHex"/>: the HMAC is always
    /// computed and compared with <see cref="CryptographicOperations.FixedTimeEquals"/>, whatever the stored value holds.
    /// </summary>
    public bool CodeMatches(string? storedHex, Guid challengeId, string code)
    {
        var expected = TryFromHex(storedHex);
        var actual = CodeHmac(challengeId, code);
        var equal = CryptographicOperations.FixedTimeEquals(expected ?? new byte[HmacLength], actual);
        return equal && expected is not null;
    }

    /// <summary>Keyed hash of a client address (label <c>ip:</c>), or null when there is none. Never the address itself.</summary>
    public string? IpHash(string? clientIp) =>
        string.IsNullOrWhiteSpace(clientIp)
            ? null
            : Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("ip:" + clientIp.Trim())));

    private byte[] CodeHmac(Guid challengeId, string code) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(challengeId.ToString("D") + ":" + code));

    private static byte[]? TryFromHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != HmacLength * 2)
            return null;
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>HCM-030 (PLAN-30 H2): refuses to start a Production host without <c>Recruitment:Portal:OtpHmacKey</c>.</summary>
public sealed class CandidateOtpStartupValidator(IConfiguration configuration, IHostEnvironment environment) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var failure = Evaluate(configuration, environment);
        if (failure is not null)
            throw new InvalidOperationException("HCM-030 candidate portal: " + failure);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Startup failure, or null when the configuration may run in this environment.</summary>
    public static string? Evaluate(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsProduction() && CandidateOtpKeyRing.Read(configuration) is null
            ? $"{CandidateOtpKeyRing.ConfigurationKey} (at least {CandidateOtpKeyRing.MinKeyLength} characters) is required in Production."
            : null;
}
