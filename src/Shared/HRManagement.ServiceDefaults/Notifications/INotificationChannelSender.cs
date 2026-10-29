using HRManagement.SharedKernel.Notifications;

namespace HRManagement.ServiceDefaults.Notifications;

/// <summary>
/// COM-030 (PLAN-30 F1): sends one already rendered message to an external address (email, SMS). Beside
/// <see cref="INotificationService"/>, which is the in-app inbox keyed by a user id: external candidates have no user id,
/// so email/SMS are not forced into that contract (deviation recorded in docs/COM_001_NOTIFICATIONS.md).
/// <para>Implementations never throw for delivery problems (only for caller cancellation): they return a
/// <see cref="ChannelSendResult"/> whose reasons are sanitized (no address, no body, no provider message text).</para>
/// </summary>
public interface INotificationChannelSender
{
    NotificationChannel Channel { get; }

    Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct);
}

/// <summary>
/// COM-030: a rendered message ready for a channel. <see cref="RecipientAddress"/> is the real address (email, or a phone
/// in E.164 form) and must never be logged, traced or stored outside the source row.
/// </summary>
/// <param name="RecipientAddress">Email address or E.164 phone number.</param>
/// <param name="Subject">Email subject (ignored by SMS).</param>
/// <param name="Body">Plain text body.</param>
/// <param name="IdempotencyKey">Stable key of the message (e.g. <c>{eventId}:{channel}</c>); a resend uses the same key.</param>
/// <param name="CorrelationId">Optional correlation id (e.g. the notification row id), safe to log.</param>
/// <param name="TemplateCode">Optional template code (e.g. <c>APP_RECEIVED</c>), safe to log.</param>
public sealed record OutboundNotification(
    string RecipientAddress,
    string? Subject,
    string Body,
    string IdempotencyKey,
    string? CorrelationId = null,
    string? TemplateCode = null);

/// <summary>
/// COM-030: outcome of <see cref="INotificationChannelSender.SendAsync"/> (modelled on <c>PayrollGradeLookupResult</c>).
/// <list type="bullet">
/// <item><see cref="Sent"/> — accepted by the provider (at-least-once: a retry after a lost answer may duplicate).</item>
/// <item><see cref="Transient"/> — retry later (timeout, connection, 408/429/5xx, SMTP 4xx).</item>
/// <item><see cref="Permanent"/> — retrying cannot help (4xx, SMTP 5xx, invalid address).</item>
/// <item><see cref="NotConfigured"/> — the channel is off or incomplete (Provider=None, missing host/endpoint, FileDrop in
/// Production); the caller keeps the message pending and never marks it sent.</item>
/// </list>
/// </summary>
public abstract record ChannelSendResult
{
    private ChannelSendResult()
    {
    }

    /// <summary>Delivered to the provider; <paramref name="ProviderMessageId"/> when the provider returned one.</summary>
    public sealed record Sent(string? ProviderMessageId) : ChannelSendResult;

    /// <summary>Temporary failure; <paramref name="Reason"/> is sanitized (e.g. <c>HTTP 503</c>, <c>Timeout</c>).</summary>
    public sealed record Transient(string Reason) : ChannelSendResult
    {
        // Declared explicitly: otherwise the positional parameter binds to the inherited Reason (infinite recursion).
        public new string Reason { get; init; } = Reason;
    }

    /// <summary>Final failure; <paramref name="Reason"/> is sanitized (e.g. <c>HTTP 400</c>, <c>SMTP 550</c>).</summary>
    public sealed record Permanent(string Reason) : ChannelSendResult
    {
        // Declared explicitly: otherwise the positional parameter binds to the inherited Reason (infinite recursion).
        public new string Reason { get; init; } = Reason;
    }

    /// <summary>The channel cannot send in this environment/configuration.</summary>
    public sealed record NotConfigured(string Reason) : ChannelSendResult
    {
        // Declared explicitly: otherwise the positional parameter binds to the inherited Reason (infinite recursion).
        public new string Reason { get; init; } = Reason;
    }

    /// <summary>Sanitized failure reason (null when sent).</summary>
    public string? Reason => this switch
    {
        Transient t => t.Reason,
        Permanent p => p.Reason,
        NotConfigured n => n.Reason,
        _ => null
    };

    /// <summary>Exception → sanitized reason: never <c>ex.Message</c> (it can carry addresses or provider text).</summary>
    internal static string SanitizedReason(Exception ex) =>
        ex is OperationCanceledException or global::Polly.Timeout.TimeoutRejectedException ? "Timeout" : ex.GetType().Name;
}
