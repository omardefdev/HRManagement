using System.Net.Sockets;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Notifications;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HRManagement.ServiceDefaults.Notifications;

/// <summary>
/// COM-030 (PLAN-30 F1): email over SMTP with MailKit (MIT). One connection per message; STARTTLS required by default,
/// implicit TLS on port 465 when <c>UseStartTls=false</c>, plain SMTP only outside Production (lab relay).
/// <para>Idempotency: the <c>Message-ID</c> is derived from the idempotency key, so a resend carries the same id, but SMTP
/// has no server-side de-duplication — a retry after a lost answer may deliver twice (at-least-once).</para>
/// <para>SMTP 5xx and malformed addresses → Permanent; 4xx, connection and timeout errors → Transient. Reasons are
/// sanitized (<c>SMTP 550</c>, exception type) — never the server text, which may echo the address.</para>
/// </summary>
public sealed class SmtpEmailNotificationSender(
    IOptions<CandidateNotificationOptions> options,
    IHostEnvironment environment) : INotificationChannelSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct)
    {
        var smtp = options.Value.Smtp;
        if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.From))
            return new ChannelSendResult.NotConfigured("SMTP host or sender not configured");

        var socketOptions = SocketOptionsFor(smtp);
        if (socketOptions == SecureSocketOptions.None && environment.IsProduction())
            return new ChannelSendResult.NotConfigured("Plain SMTP is not allowed in Production");

        MimeMessage mime;
        try
        {
            mime = BuildMessage(smtp.From, message);
        }
        catch (ParseException)
        {
            return new ChannelSendResult.Permanent("Invalid address");
        }

        using var client = new SmtpClient { Timeout = Math.Clamp(smtp.TimeoutSeconds, 5, 120) * 1000 };
        try
        {
            await client.ConnectAsync(smtp.Host, smtp.Port, socketOptions, ct);
            if (!string.IsNullOrWhiteSpace(smtp.User))
                await client.AuthenticateAsync(smtp.User, smtp.Password ?? string.Empty, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(quit: true, ct);
            return new ChannelSendResult.Sent(mime.MessageId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SmtpCommandException ex)
        {
            var code = (int)ex.StatusCode;
            return code >= 500
                ? new ChannelSendResult.Permanent($"SMTP {code}")
                : new ChannelSendResult.Transient($"SMTP {code}");
        }
        catch (AuthenticationException ex)
        {
            return new ChannelSendResult.Permanent(ChannelSendResult.SanitizedReason(ex));
        }
        catch (Exception ex) when (ex is SmtpProtocolException or IOException or SocketException
                                       or ServiceNotConnectedException or ServiceNotAuthenticatedException
                                       or SslHandshakeException or OperationCanceledException or TimeoutException)
        {
            return new ChannelSendResult.Transient(ChannelSendResult.SanitizedReason(ex));
        }
    }

    internal static SecureSocketOptions SocketOptionsFor(SmtpChannelOptions smtp) =>
        smtp.UseStartTls ? SecureSocketOptions.StartTls
        : smtp.Port == 465 ? SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.None;

    /// <summary>Plain-text UTF-8 message; <c>Message-ID</c> = <c>{sha256(key)[..32]}@{sender domain}</c>.</summary>
    internal static MimeMessage BuildMessage(string from, OutboundNotification message)
    {
        var sender = MailboxAddress.Parse(from);
        var mime = new MimeMessage();
        mime.From.Add(sender);
        mime.To.Add(MailboxAddress.Parse(message.RecipientAddress));
        mime.Subject = message.Subject ?? string.Empty;
        mime.Body = new TextPart(MimeKit.Text.TextFormat.Plain) { Text = message.Body };
        var domain = sender.Domain is { Length: > 0 } d ? d : "localhost";
        var hash = PiiMask.Sha256Hex(message.IdempotencyKey);
        mime.MessageId = $"{(hash.Length >= 32 ? hash[..32] : Guid.NewGuid().ToString("N"))}@{domain}";
        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
            mime.Headers.Add("X-Correlation-ID", message.CorrelationId);
        mime.Headers.Add("Auto-Submitted", "auto-generated");
        return mime;
    }
}
