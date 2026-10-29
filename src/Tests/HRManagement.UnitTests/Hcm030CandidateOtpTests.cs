using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Middleware;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Notifications;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Notifications;
using HRManagement.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-030 (PLAN-30 H1–H3): <see cref="CandidateOtpService"/> (neutral answer, silent refusals, resend window, one active
/// challenge, HMAC only, redacted terminal notification row, FileDrop copy, attempts → lock + audit, success consumes, no
/// status change), the key ring (constant-time compare), the startup check, the client address resolver and the chained
/// <c>careers-otp</c> limiter. SQLite, no interceptors.
/// </summary>
public sealed class Hcm030CandidateOtpTests : IDisposable
{
    private const string Email = "candidate@test.sy";
    private const string Phone = "0999123456";
    private const string PhoneE164 = "+963999123456";
    private const string NationalId = "0101010101";
    private const string ClientIp = "203.0.113.7";

    private static readonly byte[] Key = Encoding.UTF8.GetBytes("unit-test-otp-hmac-key-0123456789abcdef");
    private static readonly Regex SixDigits = new(@"\b\d{6}\b", RegexOptions.None, TimeSpan.FromSeconds(1));

    private readonly string folder = Path.Combine(Path.GetTempPath(), "hcm030-otp-" + Guid.NewGuid().ToString("N"));
    private readonly CandidateOtpKeyRing keys = new(Key);

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    // ------------------------------------------------------------------ request: neutral answer

    [Fact]
    public async Task Request_answers_the_same_neutral_message_for_unknown_wrong_locked_no_contact_and_no_provider()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);
        var sms = new FakeSender(NotificationChannel.Sms);
        var otp = Service(db, sender, sms);

        var unknown = await otp.RequestAsync("CA-NOPE", NationalId, ClientIp);
        var wrongNid = await otp.RequestAsync(citizen.ReferenceNumber, "9999999999", ClientIp);
        var empty = await otp.RequestAsync(" ", null, ClientIp);
        var noProvider = await Service(db).RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);

        var tracked = await db.CitizenApplications.SingleAsync(c => c.Id == citizen.Id);
        tracked.PortalLockedUntil = DateTime.UtcNow.AddMinutes(10);
        await db.SaveChangesAsync();
        var locked = await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);

        tracked.PortalLockedUntil = null;
        tracked.Email = null;
        tracked.Phone = null;
        await db.SaveChangesAsync();
        var noContact = await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);

        Assert.Equal(CandidateOtpRequestOutcome.NotFound, unknown.Outcome);
        Assert.Equal(CandidateOtpRequestOutcome.NotFound, wrongNid.Outcome);
        Assert.Equal(CandidateOtpRequestOutcome.NotFound, empty.Outcome);
        Assert.Equal(CandidateOtpRequestOutcome.NoSender, noProvider.Outcome);
        Assert.Equal(CandidateOtpRequestOutcome.Locked, locked.Outcome);
        Assert.Equal(CandidateOtpRequestOutcome.NoContact, noContact.Outcome);
        foreach (var result in new[] { unknown, wrongNid, empty, noProvider, locked, noContact })
        {
            Assert.Equal(CandidateOtpService.NeutralMessage, result.Message);
            Assert.Null(result.ChallengeId);
        }
        Assert.Equal("إذا كانت البيانات صحيحة فسيصلك رمز تحقق على وسيلة التواصل المسجلة.", CandidateOtpService.NeutralMessage);
        Assert.Empty(sender.Messages);
        Assert.Empty(sms.Messages);
        Assert.Empty(await db.CandidateOtpChallenges.ToListAsync());
        Assert.Empty(await db.CandidateNotifications.Where(n => n.TemplateCode == CandidateOtpService.TemplateCode).ToListAsync());
    }

    [Fact]
    public async Task Request_sends_a_six_digit_code_on_the_preferred_channel_and_stores_only_its_hmac()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Sms);
        var email = new FakeSender(NotificationChannel.Email);
        var sms = new FakeSender(NotificationChannel.Sms);
        var before = DateTime.UtcNow;

        var result = await Service(db, email, sms).RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);

        Assert.Equal(CandidateOtpRequestOutcome.Sent, result.Outcome);
        Assert.Equal(CandidateOtpService.NeutralMessage, result.Message);
        Assert.Empty(email.Messages);
        var message = Assert.Single(sms.Messages);
        Assert.Equal(PhoneE164, message.RecipientAddress);
        Assert.Null(message.Subject);
        Assert.True(message.Body.Length <= CandidateNotificationTemplates.SmsMaxLength, message.Body);
        var code = SixDigits.Match(message.Body).Value;
        Assert.Matches("^[0-9]{6}$", code);

        var challenge = await db.CandidateOtpChallenges.AsNoTracking().SingleAsync();
        Assert.Equal(result.ChallengeId, challenge.Id);
        Assert.Equal(citizen.Id, challenge.CitizenApplicationId);
        Assert.Equal(citizen.TenantId, challenge.TenantId);
        Assert.Equal(NotificationChannel.Sms, challenge.Channel);
        Assert.Equal(0, challenge.Attempts);
        Assert.Null(challenge.ConsumedAt);
        Assert.InRange(challenge.ExpiresAt, before.AddMinutes(10).AddSeconds(-5), DateTime.UtcNow.AddMinutes(10).AddSeconds(5));
        // HMAC, not the code, not a plain hash of it.
        Assert.Equal(64, challenge.CodeHmac.Length);
        Assert.DoesNotContain(code, challenge.CodeHmac, StringComparison.Ordinal);
        Assert.NotEqual(PiiMask.Sha256Hex(code), challenge.CodeHmac);
        Assert.Equal(keys.CodeHmacHex(challenge.Id, code), challenge.CodeHmac);
        Assert.NotEqual(new CandidateOtpKeyRing(Encoding.UTF8.GetBytes(new string('k', 40))).CodeHmacHex(challenge.Id, code), challenge.CodeHmac);
        // Request address: keyed hash only.
        Assert.Equal(keys.IpHash(ClientIp), challenge.RequestIp);
        Assert.DoesNotContain(ClientIp, challenge.RequestIp!, StringComparison.Ordinal);
        Assert.NotEqual(PiiMask.Sha256Hex(ClientIp), challenge.RequestIp);
    }

    [Fact]
    public async Task Request_falls_back_to_the_other_channel_when_the_preferred_contact_is_missing()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var tracked = await db.CitizenApplications.SingleAsync(c => c.Id == citizen.Id);
        tracked.Email = null;
        await db.SaveChangesAsync();
        var email = new FakeSender(NotificationChannel.Email);
        var sms = new FakeSender(NotificationChannel.Sms);

        var result = await Service(db, email, sms).RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);

        Assert.Equal(CandidateOtpRequestOutcome.Sent, result.Outcome);
        Assert.Empty(email.Messages);
        Assert.Equal(PhoneE164, Assert.Single(sms.Messages).RecipientAddress);
    }

    [Fact]
    public async Task Request_within_the_resend_window_is_refused_silently_and_a_later_one_replaces_the_active_challenge()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);
        var otp = Service(db, sender);

        var first = await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        var tooSoon = await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);

        Assert.Equal(CandidateOtpRequestOutcome.Sent, first.Outcome);
        Assert.Equal(CandidateOtpRequestOutcome.ResendTooSoon, tooSoon.Outcome);
        Assert.Equal(CandidateOtpService.NeutralMessage, tooSoon.Message);
        Assert.Single(sender.Messages);
        Assert.Single(await db.CandidateOtpChallenges.ToListAsync());

        // 61 seconds later (Recruitment:Portal:OtpResendSeconds = 60) a new code is sent and the first stops working.
        await BackdateChallengesAsync(db, TimeSpan.FromSeconds(61));
        var firstCode = CodeOf(sender.Messages[0]);
        var second = await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        Assert.Equal(CandidateOtpRequestOutcome.Sent, second.Outcome);
        var secondCode = CodeOf(sender.Messages[1]);

        var challenges = await db.CandidateOtpChallenges.AsNoTracking().ToListAsync();
        Assert.Equal(2, challenges.Count);
        Assert.Single(challenges, c => c.IsActive(DateTime.UtcNow));
        Assert.Equal(second.ChallengeId, challenges.Single(c => c.IsActive(DateTime.UtcNow)).Id);

        if (firstCode != secondCode)
            Assert.False((await otp.VerifyAsync(citizen.ReferenceNumber, firstCode, ClientIp)).Succeeded);
        Assert.True((await otp.VerifyAsync(citizen.ReferenceNumber, secondCode, ClientIp)).Succeeded);
    }

    // ------------------------------------------------------------------ request: message history and FileDrop

    [Fact]
    public async Task Request_records_one_terminal_notification_row_with_the_code_redacted()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, app) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);

        var result = await Service(db, sender).RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        var message = Assert.Single(sender.Messages);
        var code = CodeOf(message);

        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync(n => n.TemplateCode == CandidateOtpService.TemplateCode);
        Assert.Equal(app.Id, row.RecruitmentApplicationId);
        Assert.Equal(citizen.TenantId, row.TenantId);
        Assert.Null(row.EventId);
        Assert.Equal(CandidateOtpService.EventType, row.EventType);
        Assert.Equal(NotificationChannel.Email, row.Channel);
        Assert.Equal(CandidateNotificationStatus.Sent, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.NextAttemptAt);
        Assert.NotNull(row.SentAt);
        Assert.Equal(CandidateOtpService.EmailSubject, row.Subject);
        Assert.Equal(CandidateOtpService.IdempotencyKeyFor(result.ChallengeId!.Value, NotificationChannel.Email), row.IdempotencyKey);
        Assert.Equal(row.IdempotencyKey, message.IdempotencyKey);
        Assert.Equal(PiiMask.Email(Email), row.RecipientMasked);
        Assert.DoesNotContain(Email, row.RecipientMasked, StringComparison.Ordinal);
        Assert.DoesNotContain(code, row.Body, StringComparison.Ordinal);
        Assert.Contains(CandidateOtpService.RedactedCode, row.Body, StringComparison.Ordinal);
        Assert.Contains(code, message.Body, StringComparison.Ordinal);

        // Never picked up by the dispatcher (only Pending rows are sent).
        var dispatched = await new CandidateNotificationDispatcher(
                db, Registry(sender), Options.Create(new RecruitmentOptions()), NullLogger<CandidateNotificationDispatcher>.Instance)
            .SendDueAsync(DateTime.UtcNow.AddHours(1), CancellationToken.None);
        Assert.Equal(0, dispatched.Sent + dispatched.Failed + dispatched.Retried);
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task Request_with_a_failing_channel_still_answers_neutrally_and_records_a_failed_row()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email)
        {
            Next = m => new ChannelSendResult.Permanent($"SMTP 550 {m.RecipientAddress}")
        };

        var result = await Service(db, sender).RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);

        Assert.Equal(CandidateOtpRequestOutcome.SendFailed, result.Outcome);
        Assert.Equal(CandidateOtpService.NeutralMessage, result.Message);
        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync(n => n.TemplateCode == CandidateOtpService.TemplateCode);
        Assert.Equal(CandidateNotificationStatus.Failed, row.Status);
        Assert.Null(row.NextAttemptAt);
        Assert.DoesNotContain(Email, row.LastError!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileDrop_copy_holds_the_plain_code_while_the_database_does_not()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var options = Options.Create(new CandidateNotificationOptions { Provider = "FileDrop", FileDrop = { Path = folder } });
        var env = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Acceptance" && e.ContentRootPath == folder);
        var fileDrop = new FileDropNotificationSender(NotificationChannel.Email, options, env);
        var otp = new CandidateOtpService(
            db, new NotificationChannelSenderRegistry([fileDrop], options), keys,
            Options.Create(new RecruitmentOptions()), NullLogger<CandidateOtpService>.Instance);

        var result = await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);

        Assert.Equal(CandidateOtpRequestOutcome.Sent, result.Outcome);
        var key = CandidateOtpService.IdempotencyKeyFor(result.ChallengeId!.Value, NotificationChannel.Email);
        using var file = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(Path.Combine(folder, FileDropNotificationSender.FileNameFor(key))));
        var body = file.RootElement.GetProperty("body").GetString()!;
        Assert.Equal(Email, file.RootElement.GetProperty("recipient").GetString());
        Assert.Equal(CandidateOtpService.TemplateCode, file.RootElement.GetProperty("template").GetString());
        var code = SixDigits.Match(body).Value;
        Assert.Matches("^[0-9]{6}$", code);

        var row = await db.CandidateNotifications.AsNoTracking().SingleAsync(n => n.IdempotencyKey == key);
        Assert.DoesNotContain(code, row.Body, StringComparison.Ordinal);
        Assert.True((await otp.VerifyAsync(citizen.ReferenceNumber, code, ClientIp)).Succeeded);
    }

    // ------------------------------------------------------------------ verify

    [Fact]
    public async Task Verify_success_consumes_the_challenge_and_never_touches_the_application_status()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, app) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);
        var otp = Service(db, sender);
        await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        var code = CodeOf(sender.Messages.Single());

        var ok = await otp.VerifyAsync("  " + citizen.ReferenceNumber + " ", code, ClientIp);
        var replay = await otp.VerifyAsync(citizen.ReferenceNumber, code, ClientIp);

        Assert.True(ok.Succeeded);
        Assert.Equal(citizen.Id, ok.CitizenApplicationId);
        Assert.Equal(citizen.TenantId, ok.TenantId);
        Assert.False(replay.Succeeded);
        Assert.Equal(CandidateOtpService.InvalidCodeMessage, replay.Message);
        var challenge = await db.CandidateOtpChallenges.AsNoTracking().SingleAsync();
        Assert.NotNull(challenge.ConsumedAt);
        Assert.Equal(0, challenge.Attempts);

        var citizenAfter = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == citizen.Id);
        var appAfter = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == app.Id);
        Assert.Equal(citizen.Status, citizenAfter.Status);
        Assert.Equal(app.Status, appAfter.Status);
        Assert.Null(citizenAfter.PortalLockedUntil);
        Assert.Null(TenantIdFlow.Get());
    }

    [Fact]
    public async Task Wrong_codes_lock_the_portal_at_max_attempts_and_audit_without_pii()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);
        var otp = Service(db, sender);
        await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        var code = CodeOf(sender.Messages.Single());
        var wrong = WrongCode(code);

        for (var i = 1; i <= 4; i++)
        {
            var miss = await otp.VerifyAsync(citizen.ReferenceNumber, wrong, ClientIp);
            Assert.Equal(CandidateOtpVerifyOutcome.Invalid, miss.Outcome);
            Assert.Equal(i, (await db.CandidateOtpChallenges.AsNoTracking().SingleAsync()).Attempts);
        }
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == CandidateOtpService.LockedAuditAction).ToListAsync());

        var before = DateTime.UtcNow;
        var fifth = await otp.VerifyAsync(citizen.ReferenceNumber, wrong, ClientIp);

        Assert.Equal(CandidateOtpVerifyOutcome.Locked, fifth.Outcome);
        Assert.Equal(CandidateOtpService.LockedMessage, fifth.Message);
        var locked = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == citizen.Id);
        Assert.InRange(locked.PortalLockedUntil!.Value, before.AddMinutes(30).AddSeconds(-5), DateTime.UtcNow.AddMinutes(30).AddSeconds(5));
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == CandidateOtpService.LockedAuditAction);
        Assert.Equal(nameof(CitizenApplication), audit.EntityType);
        Assert.Equal(citizen.Id, audit.EntityId);
        Assert.Equal(citizen.TenantId, audit.TenantId);
        Assert.Null(audit.UserId);
        using (var json = JsonDocument.Parse(audit.ChangedFields!))
            Assert.Equal("Candidate", json.RootElement.GetProperty("actor").GetString());
        foreach (var pii in new[] { NationalId, Email, "999123456", citizen.FullName, citizen.ReferenceNumber, code, wrong })
            Assert.DoesNotContain(pii, audit.ChangedFields!, StringComparison.Ordinal);

        // Locked: even the right code is refused, and a new code is not sent (neutral answer).
        Assert.Equal(CandidateOtpVerifyOutcome.Locked, (await otp.VerifyAsync(citizen.ReferenceNumber, code, ClientIp)).Outcome);
        await BackdateChallengesAsync(db, TimeSpan.FromMinutes(5));
        var request = await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        Assert.Equal(CandidateOtpRequestOutcome.Locked, request.Outcome);
        Assert.Equal(CandidateOtpService.NeutralMessage, request.Message);
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task Wrong_codes_count_across_new_challenges_within_the_lock_window()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);
        var otp = Service(db, sender, portal: p => p.OtpResendSeconds = 0);

        await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        for (var i = 0; i < 3; i++)
            await otp.VerifyAsync(citizen.ReferenceNumber, WrongCode(CodeOf(sender.Messages[^1])), ClientIp);
        await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        var fourth = await otp.VerifyAsync(citizen.ReferenceNumber, WrongCode(CodeOf(sender.Messages[^1])), ClientIp);
        var fifth = await otp.VerifyAsync(citizen.ReferenceNumber, WrongCode(CodeOf(sender.Messages[^1])), ClientIp);

        Assert.Equal(2, sender.Messages.Count);
        Assert.Equal(CandidateOtpVerifyOutcome.Invalid, fourth.Outcome);
        Assert.Equal(CandidateOtpVerifyOutcome.Locked, fifth.Outcome);
    }

    [Fact]
    public async Task Wrong_codes_and_consumption_are_atomic_across_requests_that_read_stale_rows()
    {
        // Request A's context still holds the challenge as it was when A's scope read it (Attempts 0, unconsumed), the way
        // a concurrent request does; request B counts four wrong codes meanwhile. A's wrong code must still be the fifth.
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);
        var otpA = Service(db, sender);
        await otpA.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        var wrong = WrongCode(CodeOf(sender.Messages.Single()));
        Assert.Equal(0, (await db.CandidateOtpChallenges.SingleAsync()).Attempts); // tracked in A
        await using (var dbB = SecondContext(db))
        {
            var otpB = Service(dbB, sender);
            for (var i = 0; i < 4; i++)
                Assert.Equal(CandidateOtpVerifyOutcome.Invalid, (await otpB.VerifyAsync(citizen.ReferenceNumber, wrong, ClientIp)).Outcome);
        }

        var fifth = await otpA.VerifyAsync(citizen.ReferenceNumber, wrong, ClientIp);

        Assert.Equal(CandidateOtpVerifyOutcome.Locked, fifth.Outcome);
        Assert.Equal(5, (await db.CandidateOtpChallenges.AsNoTracking().SingleAsync()).Attempts);
        Assert.NotNull((await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == citizen.Id)).PortalLockedUntil);
        Assert.Single(await db.AuditLogs.Where(a => a.Action == CandidateOtpService.LockedAuditAction).ToListAsync());
    }

    [Fact]
    public async Task A_code_signs_in_once_even_when_another_request_read_it_unconsumed()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);
        var otpA = Service(db, sender);
        await otpA.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        var code = CodeOf(sender.Messages.Single());
        Assert.Null((await db.CandidateOtpChallenges.SingleAsync()).ConsumedAt); // tracked in A

        CandidateOtpVerifyResult first;
        await using (var dbB = SecondContext(db))
            first = await Service(dbB, sender).VerifyAsync(citizen.ReferenceNumber, code, ClientIp);
        var second = await otpA.VerifyAsync(citizen.ReferenceNumber, code, ClientIp);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(CandidateOtpService.InvalidCodeMessage, second.Message);
        Assert.Null(second.CitizenApplicationId);
    }

    [Fact]
    public async Task Resend_window_is_claimed_on_the_citizen_row_not_read_from_the_challenges()
    {
        // Another request claimed the window (its challenge row is not visible yet): this one sends nothing.
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);
        var claimedAt = DateTime.UtcNow;
        await db.CitizenApplications.Where(c => c.Id == citizen.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.PortalOtpRequestedAt, claimedAt));

        var refused = await Service(db, sender).RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);

        Assert.Equal(CandidateOtpRequestOutcome.ResendTooSoon, refused.Outcome);
        Assert.Equal(CandidateOtpService.NeutralMessage, refused.Message);
        Assert.Empty(sender.Messages);
        Assert.Empty(await db.CandidateOtpChallenges.ToListAsync());

        var stillRefused = await Service(db, sender).RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        Assert.Equal(CandidateOtpRequestOutcome.ResendTooSoon, stillRefused.Outcome);
        await BackdateChallengesAsync(db, TimeSpan.FromSeconds(61));
        var afterWindow = await Service(db, sender).RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        Assert.Equal(CandidateOtpRequestOutcome.Sent, afterWindow.Outcome);
        var stamped = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == citizen.Id);
        Assert.True(stamped.PortalOtpRequestedAt > claimedAt.AddSeconds(-1));
    }

    [Fact]
    public async Task Verify_ignores_malformed_codes_without_counting_and_accepts_arabic_indic_digits()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var sender = new FakeSender(NotificationChannel.Email);
        var otp = Service(db, sender);
        await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        var code = CodeOf(sender.Messages.Single());

        foreach (var bad in new[] { "", "12345", "1234567", "12a456", "12 3456" })
            Assert.Equal(CandidateOtpVerifyOutcome.Invalid, (await otp.VerifyAsync(citizen.ReferenceNumber, bad, ClientIp)).Outcome);
        Assert.Equal(0, (await db.CandidateOtpChallenges.AsNoTracking().SingleAsync()).Attempts);

        Assert.Equal("123456", CandidateOtpService.NormalizeCode(" ١٢٣٤٥٦ "));
        Assert.Equal("123456", CandidateOtpService.NormalizeCode("۱۲۳۴۵۶"));
        var arabic = string.Concat(code.Select(c => (char)('٠' + (c - '0'))));
        Assert.True((await otp.VerifyAsync(citizen.ReferenceNumber, arabic, ClientIp)).Succeeded);
    }

    [Fact]
    public async Task Unknown_reference_expired_challenge_and_wrong_code_get_the_same_answer()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await ApplyAsync(db, NotificationChannel.Email);
        var (other, _) = await ApplyAsync(db, NotificationChannel.Email, "0202020202");
        var sender = new FakeSender(NotificationChannel.Email);
        var otp = Service(db, sender);
        await otp.RequestAsync(citizen.ReferenceNumber, NationalId, ClientIp);
        var code = CodeOf(sender.Messages.Single());

        var unknown = await otp.VerifyAsync("CA-NOPE", code, ClientIp);
        var noChallenge = await otp.VerifyAsync(other.ReferenceNumber, code, ClientIp);
        var wrong = await otp.VerifyAsync(citizen.ReferenceNumber, WrongCode(code), ClientIp);
        var challenge = await db.CandidateOtpChallenges.SingleAsync();
        challenge.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        var expired = await otp.VerifyAsync(citizen.ReferenceNumber, code, ClientIp);

        foreach (var result in new[] { unknown, noChallenge, wrong, expired })
        {
            Assert.Equal(CandidateOtpVerifyOutcome.Invalid, result.Outcome);
            Assert.Equal(CandidateOtpService.InvalidCodeMessage, result.Message);
            Assert.Null(result.CitizenApplicationId);
            Assert.Null(result.TenantId);
        }
    }

    // ------------------------------------------------------------------ key ring and startup

    [Fact]
    public void Code_check_is_hmac_based_and_constant_time()
    {
        var id = Guid.NewGuid();
        var stored = keys.CodeHmacHex(id, "123456");

        Assert.True(keys.CodeMatches(stored, id, "123456"));
        Assert.False(keys.CodeMatches(stored, id, "123457"));
        Assert.False(keys.CodeMatches(stored, Guid.NewGuid(), "123456"));
        Assert.False(keys.CodeMatches(stored.ToUpperInvariant()[..10], id, "123456"));
        Assert.False(keys.CodeMatches(null, id, "123456"));
        Assert.False(keys.CodeMatches(new string('z', 64), id, "123456"));
        Assert.False(keys.CodeMatches(PiiMask.Sha256Hex("123456"), id, "123456"));

        var source = System.IO.File.ReadAllText(RepoPaths.Resolve(
            "src", "Services", "HRCore", "HR.Core.Infrastructure", "Services", "Recruitment", "CandidateOtpKeyRing.cs"));
        Assert.Contains("CryptographicOperations.FixedTimeEquals", source, StringComparison.Ordinal);
        Assert.Contains("HMACSHA256.HashData", source, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new CandidateOtpKeyRing(new byte[8]));
    }

    [Fact]
    public async Task Otp_key_is_required_in_Production_only()
    {
        static IConfiguration Config(string? key) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [CandidateOtpKeyRing.ConfigurationKey] = key }).Build();
        static IHostEnvironment Env(string name) => Mock.Of<IHostEnvironment>(e => e.EnvironmentName == name);

        Assert.NotNull(CandidateOtpStartupValidator.Evaluate(Config(null), Env(Environments.Production)));
        Assert.NotNull(CandidateOtpStartupValidator.Evaluate(Config("too-short"), Env(Environments.Production)));
        Assert.Null(CandidateOtpStartupValidator.Evaluate(Config(new string('x', 32)), Env(Environments.Production)));
        Assert.Null(CandidateOtpStartupValidator.Evaluate(Config(null), Env(Environments.Development)));
        Assert.Null(CandidateOtpStartupValidator.Evaluate(Config(null), Env("Acceptance")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CandidateOtpStartupValidator(Config(null), Env(Environments.Production)).StartAsync(CancellationToken.None));

        var fallback = new CandidateOtpKeyRing(Config(null), NullLogger<CandidateOtpKeyRing>.Instance);
        Assert.False(fallback.IsConfigured);
        var configured = new CandidateOtpKeyRing(Config(new string('x', 40)), NullLogger<CandidateOtpKeyRing>.Instance);
        Assert.True(configured.IsConfigured);
        var id = Guid.NewGuid();
        Assert.Equal(
            new CandidateOtpKeyRing(Encoding.UTF8.GetBytes(new string('x', 40))).CodeHmacHex(id, "000111"),
            configured.CodeHmacHex(id, "000111"));
    }

    // ------------------------------------------------------------------ client address and careers-otp limiter

    [Theory]
    [InlineData("10.0.0.5", "198.51.100.1, 203.0.113.9", "203.0.113.9")]        // gateway (private range): right-most entry
    [InlineData("172.18.0.4", "203.0.113.9:51234", "203.0.113.9")]              // with a port
    [InlineData("::ffff:10.0.0.5", "203.0.113.9", "203.0.113.9")]               // IPv4-mapped peer
    [InlineData("198.51.100.20", "203.0.113.9", "198.51.100.20")]               // untrusted peer: header ignored
    [InlineData("10.0.0.5", null, "10.0.0.5")]                                  // trusted peer, no header
    [InlineData("10.0.0.5", "not-an-ip", "10.0.0.5")]                           // garbage header
    public void Client_address_uses_the_forwarded_for_entry_only_from_a_trusted_proxy(string peer, string? forwardedFor, string expected)
    {
        var resolver = CareersClientIpResolver.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Equal(IPAddress.Parse(expected), resolver.Resolve(Http(peer, forwardedFor)));
    }

    [Fact]
    public void Client_address_trusts_configured_proxies_and_the_rewritten_address_when_forwarded_headers_run()
    {
        var onlyGateway = CareersClientIpResolver.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies"] = "192.0.2.10" }).Build());
        Assert.Equal(IPAddress.Parse("203.0.113.9"), onlyGateway.Resolve(Http("192.0.2.10", "203.0.113.9")));
        Assert.Equal(IPAddress.Parse("10.0.0.5"), onlyGateway.Resolve(Http("10.0.0.5", "203.0.113.9")));

        var applied = CareersClientIpResolver.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["UseForwardedHeaders"] = "true" }).Build());
        Assert.Equal(IPAddress.Parse("10.0.0.5"), applied.Resolve(Http("10.0.0.5", "203.0.113.9")));
        Assert.Equal("unknown", applied.ResolveKey(new DefaultHttpContext()));
    }

    [Fact]
    public void Careers_otp_limiter_partitions_by_resolved_client_address_and_operation()
    {
        using var limiter = Limiter(sharedPerMinute: 1000, requestsPerClient: 2, verifyPerClient: 3);
        // All three clients reach HR.Core through the same gateway address.
        var a = OtpContext("10.0.0.5", "203.0.113.1", CareersOtpOperation.Request);
        var b = OtpContext("10.0.0.5", "203.0.113.2", CareersOtpOperation.Request);
        var aVerify = OtpContext("10.0.0.5", "203.0.113.1", CareersOtpOperation.Verify);

        Assert.True(limiter.AttemptAcquire(a).IsAcquired);
        Assert.True(limiter.AttemptAcquire(a).IsAcquired);
        Assert.False(limiter.AttemptAcquire(a).IsAcquired);
        Assert.True(limiter.AttemptAcquire(b).IsAcquired);
        for (var i = 0; i < 3; i++)
            Assert.True(limiter.AttemptAcquire(aVerify).IsAcquired);
        Assert.False(limiter.AttemptAcquire(aVerify).IsAcquired);

        Assert.Equal("otp-request|203.0.113.1", PublicCareersRateLimitExtensions.PartitionKey(CareersOtpOperation.Request, "203.0.113.1"));
        Assert.Equal("otp-verify|203.0.113.1", PublicCareersRateLimitExtensions.PartitionKey(CareersOtpOperation.Verify, "203.0.113.1"));
    }

    [Fact]
    public void Careers_otp_endpoints_also_count_against_the_shared_public_careers_window()
    {
        using var limiter = Limiter(sharedPerMinute: 3, requestsPerClient: 100, verifyPerClient: 100);
        var track = Context("10.0.0.5", "203.0.113.1", new EnableRateLimitingAttribute(PublicCareersRateLimitExtensions.PolicyName));
        var unlimited = Context("10.0.0.5", "203.0.113.1");

        Assert.True(limiter.AttemptAcquire(track).IsAcquired);
        Assert.True(limiter.AttemptAcquire(OtpContext("10.0.0.5", "203.0.113.2", CareersOtpOperation.Request)).IsAcquired);
        Assert.True(limiter.AttemptAcquire(OtpContext("10.0.0.5", "203.0.113.3", CareersOtpOperation.Verify)).IsAcquired);
        Assert.False(limiter.AttemptAcquire(OtpContext("10.0.0.5", "203.0.113.4", CareersOtpOperation.Request)).IsAcquired);
        Assert.False(limiter.AttemptAcquire(track).IsAcquired);
        for (var i = 0; i < 10; i++)
            Assert.True(limiter.AttemptAcquire(unlimited).IsAcquired);
    }

    [Fact]
    public async Task Rate_limit_rejection_is_an_arabic_429_page_and_otp_actions_use_the_careers_otp_policy()
    {
        var services = new ServiceCollection();
        services.AddPublicCareersRateLimiting(new ConfigurationBuilder().Build());
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<RateLimiterOptions>>().Value;
        Assert.NotNull(options.GlobalLimiter);
        Assert.Equal(StatusCodes.Status429TooManyRequests, options.RejectionStatusCode);

        using var exhausted = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        });
        exhausted.AttemptAcquire();
        using var lease = exhausted.AttemptAcquire();
        var http = new DefaultHttpContext();
        http.Request.PathBase = "/careers";
        http.Response.Body = new MemoryStream();
        await options.OnRejected!(new OnRejectedContext { HttpContext = http, Lease = lease }, CancellationToken.None);

        http.Response.Body.Position = 0;
        var html = await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync();
        Assert.StartsWith("text/html", http.Response.ContentType);
        Assert.Contains("lang=\"ar\" dir=\"rtl\"", html, StringComparison.Ordinal);
        Assert.Contains("يرجى الانتظار قليلاً", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/careers/\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.True(http.Response.Headers.ContainsKey("Retry-After"));

        var controller = typeof(HR.Core.Web.Controllers.CandidatePortalController);
        foreach (var (action, operation) in new[] { ("Login", CareersOtpOperation.Request), ("Verify", CareersOtpOperation.Verify) })
        {
            var method = controller.GetMethods().Single(m => m.Name == action && m.GetCustomAttributes(typeof(HttpPostAttribute), false).Length > 0);
            Assert.Equal(PublicCareersRateLimitExtensions.OtpPolicyName,
                ((EnableRateLimitingAttribute)Attribute.GetCustomAttribute(method, typeof(EnableRateLimitingAttribute))!).PolicyName);
            Assert.Equal(operation, ((CareersOtpRateLimitAttribute)Attribute.GetCustomAttribute(method, typeof(CareersOtpRateLimitAttribute))!).Operation);
            Assert.NotNull(Attribute.GetCustomAttribute(method, typeof(ValidateAntiForgeryTokenAttribute)));
        }
    }

    // ------------------------------------------------------------------ helpers

    private CandidateOtpService Service(
        HRCoreDbContext db, FakeSender? first = null, FakeSender? second = null, Action<RecruitmentPortalOptions>? portal = null)
    {
        var options = new RecruitmentOptions();
        portal?.Invoke(options.Portal);
        var senders = new[] { first, second }.Where(s => s is not null).Cast<INotificationChannelSender>().ToArray();
        return new CandidateOtpService(
            db,
            new NotificationChannelSenderRegistry(senders, Options.Create(new CandidateNotificationOptions { Provider = senders.Length == 0 ? "None" : "FileDrop" })),
            keys,
            Options.Create(options),
            NullLogger<CandidateOtpService>.Instance);
    }

    private static NotificationChannelSenderRegistry Registry(params INotificationChannelSender[] senders) =>
        new(senders, Options.Create(new CandidateNotificationOptions { Provider = "FileDrop" }));

    internal static async Task<(CitizenApplication Citizen, RecruitmentApplication App)> ApplyAsync(
        HRCoreDbContext db, NotificationChannel? channel, string nationalId = NationalId)
    {
        if (!await db.PublicJobPostings.AnyAsync())
            await Hcm030TestSupport.SeedRequisitionAsync(db);
        var posting = await db.PublicJobPostings.AsNoTracking().FirstAsync();
        var vm = Hcm030TestSupport.ApplyModel(posting.Id, nationalId);
        vm.Phone = Phone;
        vm.Email = Email;
        vm.PreferredChannel = channel;
        vm.NotificationConsent = channel is not null;
        var result = await Hcm030TestSupport.CreatePublicCareersController(db).Apply(vm);
        Assert.IsType<RedirectToActionResult>(result);
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.NationalId == nationalId);
        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == citizen.PromotedRecruitmentApplicationId);
        return (citizen, app);
    }

    private static async Task BackdateChallengesAsync(HRCoreDbContext db, TimeSpan by)
    {
        foreach (var challenge in await db.CandidateOtpChallenges.ToListAsync())
            challenge.CreatedAt -= by;
        await db.SaveChangesAsync();
        // The resend window is claimed on the citizen row (written with ExecuteUpdate, so tracked copies are stale).
        foreach (var row in await db.CitizenApplications.AsNoTracking()
                     .Where(c => c.PortalOtpRequestedAt != null)
                     .Select(c => new { c.Id, c.PortalOtpRequestedAt })
                     .ToListAsync())
        {
            var requestedAt = row.PortalOtpRequestedAt - by;
            await db.CitizenApplications.Where(c => c.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PortalOtpRequestedAt, requestedAt));
        }
    }

    /// <summary>A second context on the same SQLite connection (another request's scope).</summary>
    private static HRCoreDbContext SecondContext(HRCoreDbContext db) =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(db.Database.GetDbConnection()).Options);

    private static string CodeOf(OutboundNotification message) => SixDigits.Match(message.Body).Value;

    private static string WrongCode(string code) => code == "000000" ? "000001" : "000000";

    private static PartitionedRateLimiter<HttpContext> Limiter(int sharedPerMinute, int requestsPerClient, int verifyPerClient) =>
        PublicCareersRateLimitExtensions.CreateGlobalLimiter(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:PublicCareersRateLimitPerMinute"] = sharedPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Recruitment:Portal:OtpRequestsPerIpPer10Min"] = requestsPerClient.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Recruitment:Portal:OtpVerifyPerIpPer10Min"] = verifyPerClient.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }).Build(),
            CareersClientIpResolver.FromConfiguration(new ConfigurationBuilder().Build()));

    private static HttpContext OtpContext(string peer, string forwardedFor, CareersOtpOperation operation) =>
        Context(peer, forwardedFor,
            new EnableRateLimitingAttribute(PublicCareersRateLimitExtensions.OtpPolicyName),
            new CareersOtpRateLimitAttribute(operation));

    private static HttpContext Context(string peer, string? forwardedFor, params object[] metadata)
    {
        var http = Http(peer, forwardedFor);
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "test"));
        return http;
    }

    private static DefaultHttpContext Http(string peer, string? forwardedFor)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        if (forwardedFor is not null)
            http.Request.Headers[CareersClientIpResolver.ForwardedForHeader] = forwardedFor;
        return http;
    }

    private sealed class FakeSender(NotificationChannel channel) : INotificationChannelSender
    {
        public NotificationChannel Channel { get; } = channel;
        public List<OutboundNotification> Messages { get; } = [];
        public Func<OutboundNotification, ChannelSendResult> Next { get; init; } = _ => new ChannelSendResult.Sent("fake-1");

        public Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct)
        {
            Messages.Add(message);
            return Task.FromResult(Next(message));
        }
    }
}
