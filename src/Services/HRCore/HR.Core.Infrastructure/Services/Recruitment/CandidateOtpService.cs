using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Notifications;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>What <see cref="CandidateOtpService.RequestAsync"/> did. Never shown to the candidate (they always get the neutral message).</summary>
public enum CandidateOtpRequestOutcome
{
    Sent,
    NotFound,
    Locked,
    NoContact,
    ResendTooSoon,
    NoSender,
    SendFailed,
    Error
}

public sealed record CandidateOtpRequestResult(string Message, CandidateOtpRequestOutcome Outcome, Guid? ChallengeId = null);

public enum CandidateOtpVerifyOutcome
{
    Verified,
    Invalid,
    Locked,
    Error
}

/// <summary>
/// Result of <see cref="CandidateOtpService.VerifyAsync"/>. On success it carries the ids the careers session is issued
/// for (taken from the database row, never from the request).
/// </summary>
public sealed record CandidateOtpVerifyResult(
    CandidateOtpVerifyOutcome Outcome,
    string? Message,
    Guid? CitizenApplicationId = null,
    Guid? TenantId = null)
{
    public bool Succeeded => Outcome == CandidateOtpVerifyOutcome.Verified;
}

/// <summary>
/// HCM-030 (PLAN-30 H2, D-4): one-time codes that open <c>/careers/my</c>.
/// <list type="bullet">
/// <item><see cref="RequestAsync"/> always answers <see cref="NeutralMessage"/>. It finds the citizen row by reference
/// number + national id (the <c>PublicCareersPortal</c> bypass; the tenant comes from the row), refuses silently when the
/// portal is locked, no contact can be used, or a code was sent less than <c>Recruitment:Portal:OtpResendSeconds</c> ago;
/// otherwise it expires the active challenge, creates a new one (6 digits, HMAC only, <c>OtpValidityMinutes</c>) and sends
/// it at once through the session-2 channel senders (preferred channel, else the other one). The linked recruitment
/// application gets one terminal <see cref="CandidateNotification"/> row with the code redacted (never re-sent by the
/// dispatcher); the FileDrop copy (lab only) holds the plain code.</item>
/// <item><see cref="VerifyAsync"/> checks a code against the active challenge of the reference in constant time. A wrong
/// code counts an attempt; <c>OtpMaxAttempts</c> wrong codes within <c>LockMinutes</c> lock the portal for
/// <c>LockMinutes</c> (audited <c>CandidatePortalLocked</c>, no PII). Success consumes the challenge.</item>
/// </list>
/// Neither touches the application status. Both run their writes with the row's tenant as the ambient tenant, so a
/// client-sent <c>X-Tenant-ID</c> cannot steer them.
/// </summary>
public sealed class CandidateOtpService(
    HRCoreDbContext db,
    NotificationChannelSenderRegistry senders,
    CandidateOtpKeyRing keys,
    IOptions<RecruitmentOptions> options,
    ILogger<CandidateOtpService> logger)
{
    public const string NeutralMessage = "إذا كانت البيانات صحيحة فسيصلك رمز تحقق على وسيلة التواصل المسجلة.";
    public const string InvalidCodeMessage = "رمز التحقق غير صحيح أو انتهت صلاحيته. يمكنك طلب رمز جديد.";
    public const string LockedMessage = "تم إيقاف الدخول مؤقتاً بسبب تكرار إدخال رمز خاطئ. يرجى المحاولة لاحقاً.";

    public const string LockedAuditAction = "CandidatePortalLocked";
    public const string TemplateCode = "OTP";
    public const string EventType = "OtpRequested";
    public const string EmailSubject = "بوابة التوظيف — رمز التحقق";
    public const string RedactedCode = "******";
    public const int CodeLength = 6;

    /// <summary>Message text (≤ one SMS segment, no PII).</summary>
    public static string MessageText(string code, int validityMinutes) =>
        $"رمز التحقق لبوابة التوظيف: {code} — صالح {validityMinutes.ToString(CultureInfo.InvariantCulture)} دقائق، لا تشاركه مع أحد.";

    /// <summary>Idempotency key of the message (also the FileDrop file name source).</summary>
    public static string IdempotencyKeyFor(Guid challengeId, NotificationChannel channel) => $"otp:{challengeId:D}:{channel}";

    public async Task<CandidateOtpRequestResult> RequestAsync(
        string? referenceNumber, string? nationalId, string? clientIp, CancellationToken ct = default)
    {
        var previousTenant = TenantIdFlow.Get();
        try
        {
            var reference = referenceNumber?.Trim();
            var nid = nationalId?.Trim();
            if (string.IsNullOrEmpty(reference) || string.IsNullOrEmpty(nid))
                return Neutral(CandidateOtpRequestOutcome.NotFound);

            var hash = PiiMask.Sha256Hex(nid);
            var application = await db.CitizenApplications
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ReferenceNumber == reference && (a.NationalIdHash == hash || a.NationalId == nid), ct);
            if (application is null)
                return Neutral(CandidateOtpRequestOutcome.NotFound);

            TenantIdFlow.Set(application.TenantId);
            var portal = options.Value.Portal;
            var now = DateTime.UtcNow;
            if (application.PortalLockedUntil > now)
                return Neutral(CandidateOtpRequestOutcome.Locked);

            var target = ChooseTarget(application);
            if (target is null)
                return Neutral(application.AnonymizedAt is null && HasAnyContact(application)
                    ? CandidateOtpRequestOutcome.NoSender
                    : CandidateOtpRequestOutcome.NoContact);
            var (channel, address, sender) = target.Value;

            // Resend window claimed atomically on the citizen row (one conditional UPDATE), so two concurrent requests
            // cannot both pass it and send two codes.
            var resendAfter = now.AddSeconds(-Math.Max(0, portal.OtpResendSeconds));
            var claimed = await Applications(application)
                .Where(a => a.PortalOtpRequestedAt == null || a.PortalOtpRequestedAt <= resendAfter)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.PortalOtpRequestedAt, now), ct);
            if (claimed == 0)
                return Neutral(CandidateOtpRequestOutcome.ResendTooSoon);

            // One active challenge: the new code replaces any previous one.
            await Challenges(application)
                .Where(c => c.ConsumedAt == null && c.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, now).SetProperty(c => c.UpdatedAt, now), ct);

            var code = NewCode();
            var challenge = new CandidateOtpChallenge
            {
                TenantId = application.TenantId,
                CitizenApplicationId = application.Id,
                Channel = channel,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = now.AddMinutes(Math.Max(1, portal.OtpValidityMinutes)),
                RequestIp = keys.IpHash(clientIp)
            };
            challenge.CodeHmac = keys.CodeHmacHex(challenge.Id, code);
            db.CandidateOtpChallenges.Add(challenge);

            var key = IdempotencyKeyFor(challenge.Id, channel);
            var text = MessageText(code, Math.Max(1, portal.OtpValidityMinutes));
            var subject = channel == NotificationChannel.Email ? EmailSubject : null;
            ChannelSendResult result;
            try
            {
                result = await sender.SendAsync(
                    new OutboundNotification(address, subject, text, key, CorrelationId: challenge.Id.ToString("D"), TemplateCode: TemplateCode),
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new ChannelSendResult.Transient(ex.GetType().Name);
            }

            // The message history lives on the recruitment application (legacy unlinked rows have none: no row).
            if (application.PromotedRecruitmentApplicationId is Guid recruitmentApplicationId)
            {
                var sent = result is ChannelSendResult.Sent;
                db.CandidateNotifications.Add(new CandidateNotification
                {
                    TenantId = application.TenantId,
                    RecruitmentApplicationId = recruitmentApplicationId,
                    EventId = null,
                    EventType = EventType,
                    Channel = channel,
                    TemplateCode = TemplateCode,
                    RecipientMasked = channel == NotificationChannel.Email ? PiiMask.Email(address) : PiiMask.Phone(address),
                    Subject = subject,
                    Body = text.Replace(code, RedactedCode, StringComparison.Ordinal),
                    IdempotencyKey = key,
                    // Terminal: the dispatcher only resends Pending rows, and this body no longer holds the code.
                    Status = sent ? CandidateNotificationStatus.Sent : CandidateNotificationStatus.Failed,
                    Attempts = 1,
                    NextAttemptAt = null,
                    SentAt = sent ? now : null,
                    ProviderMessageId = (result as ChannelSendResult.Sent)?.ProviderMessageId,
                    LastError = sent ? null : CandidateNotificationDispatcher.SanitizeError(result.Reason, address),
                    ExpiresAt = challenge.ExpiresAt
                });
            }

            await db.SaveChangesAsync(ct);
            if (result is not ChannelSendResult.Sent)
            {
                logger.LogWarning(
                    "HCM-030 OTP not delivered channel={Channel} reason={Reason}",
                    channel, CandidateNotificationDispatcher.SanitizeError(result.Reason, address));
                return Neutral(CandidateOtpRequestOutcome.SendFailed, challenge.Id);
            }

            return Neutral(CandidateOtpRequestOutcome.Sent, challenge.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // No PII: the exception type only (messages may carry values).
            logger.LogWarning("HCM-030 OTP request failed: {ErrorType}", ex.GetType().Name);
            return Neutral(CandidateOtpRequestOutcome.Error);
        }
        finally
        {
            TenantIdFlow.Set(previousTenant);
        }
    }

    public async Task<CandidateOtpVerifyResult> VerifyAsync(
        string? referenceNumber, string? code, string? clientIp, CancellationToken ct = default)
    {
        var previousTenant = TenantIdFlow.Get();
        try
        {
            var reference = referenceNumber?.Trim();
            var normalizedCode = NormalizeCode(code);
            CitizenApplication? application = null;
            if (!string.IsNullOrEmpty(reference) && normalizedCode is not null)
            {
                application = await db.CitizenApplications
                    .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.ReferenceNumber == reference, ct);
            }

            if (application is null)
            {
                // Same work as a wrong code (one HMAC + constant-time compare), so the answer time does not tell
                // whether the reference exists.
                keys.CodeMatches(null, Guid.Empty, normalizedCode ?? string.Empty);
                return Invalid();
            }

            TenantIdFlow.Set(application.TenantId);
            var portal = options.Value.Portal;
            var now = DateTime.UtcNow;
            if (application.PortalLockedUntil > now)
            {
                keys.CodeMatches(null, Guid.Empty, normalizedCode!);
                return new CandidateOtpVerifyResult(CandidateOtpVerifyOutcome.Locked, LockedMessage);
            }

            // Read without tracking and written only through conditional UPDATEs: concurrent verifies of the same
            // reference must not lose wrong-code counts or consume one code twice (no read-modify-write).
            var challenge = await Challenges(application)
                .AsNoTracking()
                .Where(c => c.ConsumedAt == null && c.ExpiresAt > now)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (challenge is null || application.AnonymizedAt is not null)
            {
                keys.CodeMatches(null, Guid.Empty, normalizedCode!);
                return Invalid();
            }

            if (keys.CodeMatches(challenge.CodeHmac, challenge.Id, normalizedCode!))
            {
                // Single use: only the request whose UPDATE still finds the challenge unconsumed and unexpired signs in.
                var consumed = await Challenges(application)
                    .Where(c => c.Id == challenge.Id && c.ConsumedAt == null && c.ExpiresAt > now)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now).SetProperty(c => c.UpdatedAt, now), ct);
                return consumed == 1
                    ? new CandidateOtpVerifyResult(CandidateOtpVerifyOutcome.Verified, null, application.Id, application.TenantId)
                    : Invalid();
            }

            await Challenges(application)
                .Where(c => c.Id == challenge.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1).SetProperty(c => c.UpdatedAt, now), ct);
            // Wrong codes count across this application's unconsumed challenges of the last LockMinutes (a new code does
            // not reset them); after the latest successful sign-in only. Summed in the database after the atomic increment,
            // so the request whose increment lands last sees every concurrent wrong code.
            var lockMinutes = Math.Max(1, portal.LockMinutes);
            var lastSuccess = await Challenges(application).MaxAsync(c => c.ConsumedAt, ct);
            var windowStart = now.AddMinutes(-lockMinutes);
            if (lastSuccess > windowStart)
                windowStart = lastSuccess.Value;
            var wrongCodes = await Challenges(application)
                .Where(c => c.ConsumedAt == null && (c.CreatedAt >= windowStart || c.Id == challenge.Id))
                .SumAsync(c => c.Attempts, ct);

            if (wrongCodes >= Math.Max(1, portal.OtpMaxAttempts))
            {
                var lockedUntil = now.AddMinutes(lockMinutes);
                // Conditional: concurrent lock-triggering requests lock (and audit) once.
                var locked = await Applications(application)
                    .Where(a => a.PortalLockedUntil == null || a.PortalLockedUntil <= now)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.PortalLockedUntil, lockedUntil).SetProperty(a => a.UpdatedAt, now), ct);
                await Challenges(application)
                    .Where(c => c.Id == challenge.Id && c.ExpiresAt > now)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, now).SetProperty(c => c.UpdatedAt, now), ct);
                if (locked == 1)
                {
                    db.AuditLogs.Add(new EntityAuditLog
                    {
                        TenantId = application.TenantId,
                        EntityType = nameof(CitizenApplication),
                        EntityId = application.Id,
                        Action = LockedAuditAction,
                        UserId = null,
                        ChangedFields = JsonSerializer.Serialize(new
                        {
                            actor = "Candidate",
                            challengeId = challenge.Id,
                            wrongCodes,
                            lockedUntil
                        })
                    });
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("HCM-030 candidate portal locked application={ApplicationId}", application.Id);
                }
                return new CandidateOtpVerifyResult(CandidateOtpVerifyOutcome.Locked, LockedMessage);
            }

            return Invalid();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("HCM-030 OTP verify failed: {ErrorType}", ex.GetType().Name);
            return new CandidateOtpVerifyResult(CandidateOtpVerifyOutcome.Error, InvalidCodeMessage);
        }
        finally
        {
            TenantIdFlow.Set(previousTenant);
        }
    }

    /// <summary>Six ASCII digits (Arabic-Indic digits and surrounding spaces accepted), or null.</summary>
    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var chars = new char[CodeLength];
        var count = 0;
        foreach (var ch in code.Trim())
        {
            char digit;
            if (ch is >= '0' and <= '9')
                digit = ch;
            else if (ch is >= '٠' and <= '٩')
                digit = (char)('0' + (ch - '٠'));
            else if (ch is >= '۰' and <= '۹')
                digit = (char)('0' + (ch - '۰'));
            else
                return null;
            if (count == CodeLength)
                return null;
            chars[count++] = digit;
        }
        return count == CodeLength ? new string(chars) : null;
    }

    private static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    private IQueryable<CandidateOtpChallenge> Challenges(CitizenApplication application) =>
        db.CandidateOtpChallenges
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .Where(c => c.TenantId == application.TenantId && c.CitizenApplicationId == application.Id);

    private IQueryable<CitizenApplication> Applications(CitizenApplication application) =>
        db.CitizenApplications
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .Where(a => a.TenantId == application.TenantId && a.Id == application.Id);

    /// <summary>
    /// Preferred channel (the one the candidate chose for notifications) when it has a contact and a sender, else the
    /// other one; without a preference email first. Null when no channel can carry the code.
    /// </summary>
    private (NotificationChannel Channel, string Address, INotificationChannelSender Sender)? ChooseTarget(CitizenApplication application)
    {
        if (application.AnonymizedAt is not null)
            return null;
        var order = application.NotificationChannel == NotificationChannel.Sms
            ? new[] { NotificationChannel.Sms, NotificationChannel.Email }
            : new[] { NotificationChannel.Email, NotificationChannel.Sms };
        foreach (var channel in order)
        {
            var address = AddressOf(application, channel);
            var sender = address is null ? null : senders.For(channel);
            if (address is not null && sender is not null)
                return (channel, address, sender);
        }
        return null;
    }

    private bool HasAnyContact(CitizenApplication application) =>
        AddressOf(application, NotificationChannel.Email) is not null || AddressOf(application, NotificationChannel.Sms) is not null;

    private string? AddressOf(CitizenApplication application, NotificationChannel channel)
    {
        if (channel == NotificationChannel.Email)
            return CandidateContactRules.IsValidEmail(application.Email) ? application.Email!.Trim() : null;
        return CandidateContactRules.TryNormalizePhone(application.Phone, options.Value.Portal.PhoneCountryCode, out var e164)
            ? e164
            : null;
    }

    private static CandidateOtpRequestResult Neutral(CandidateOtpRequestOutcome outcome, Guid? challengeId = null) =>
        new(NeutralMessage, outcome, challengeId);

    private static CandidateOtpVerifyResult Invalid() =>
        new(CandidateOtpVerifyOutcome.Invalid, InvalidCodeMessage);
}
