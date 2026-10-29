namespace HRManagement.ServiceDefaults.Notifications;

/// <summary>COM-030: which channel senders <c>Notifications:Candidate:Provider</c> selects.</summary>
public enum CandidateNotificationProvider
{
    /// <summary>No sender: messages stay Pending until they expire (and a warning is emitted). Production/Staging default.</summary>
    None = 0,

    /// <summary>Lab/acceptance: one UTF-8 JSON file per message (<see cref="FileDropNotificationSender"/>). Refused in Production.</summary>
    FileDrop = 1,

    /// <summary>Real providers: <see cref="SmtpEmailNotificationSender"/> + <see cref="HttpSmsNotificationSender"/>.</summary>
    SmtpSms = 2
}

/// <summary>
/// COM-030 (PLAN-30 F1): <c>Notifications:Candidate</c> settings. Secrets (<c>Smtp:Password</c>, <c>Sms:AuthToken</c>) come
/// only from environment variables or a secret store (<c>Notifications__Candidate__Smtp__Password</c>,
/// <c>Notifications__Candidate__Sms__AuthToken</c>), never from appsettings files.
/// </summary>
public sealed class CandidateNotificationOptions
{
    public const string SectionName = "Notifications:Candidate";

    /// <summary>
    /// <c>Notifications:Candidate:Provider</c> — <c>None</c> (default, fail-safe), <c>FileDrop</c> or <c>Smtp+Sms</c>
    /// (also accepted: <c>SmtpSms</c>). A string, because the binder cannot map <c>Smtp+Sms</c> to an enum member; unknown
    /// values mean <see cref="CandidateNotificationProvider.None"/>.
    /// </summary>
    public string Provider { get; set; } = nameof(CandidateNotificationProvider.None);

    /// <summary><c>Notifications:Candidate:MaxAttempts</c> — send attempts before a row is Failed (default 5).</summary>
    public int MaxAttempts { get; set; } = 5;

    public FileDropChannelOptions FileDrop { get; set; } = new();

    public SmtpChannelOptions Smtp { get; set; } = new();

    public SmsChannelOptions Sms { get; set; } = new();

    public CandidateNotificationProvider ProviderKind => ParseProvider(Provider);

    public static CandidateNotificationProvider ParseProvider(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace("+", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized.ToUpperInvariant() switch
        {
            "FILEDROP" => CandidateNotificationProvider.FileDrop,
            "SMTPSMS" => CandidateNotificationProvider.SmtpSms,
            _ => CandidateNotificationProvider.None
        };
    }
}

/// <summary><c>Notifications:Candidate:FileDrop:*</c>.</summary>
public sealed class FileDropChannelOptions
{
    /// <summary>
    /// <c>Notifications:Candidate:FileDrop:Path</c> — folder of the message files. Relative paths resolve against the
    /// content root. Containers run as a non-root user over a root-owned <c>/app</c>, so use an absolute, mounted, writable
    /// folder there (acceptance: <c>/var/hrm/candidate-notifications</c>, bind-mounted from
    /// <c>./artifacts/acceptance/candidate-notifications</c>).
    /// </summary>
    public string? Path { get; set; }
}

/// <summary><c>Notifications:Candidate:Smtp:*</c> (password: <c>Notifications__Candidate__Smtp__Password</c>, env/secret only).</summary>
public sealed class SmtpChannelOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>
    /// true (default): STARTTLS is required. false: implicit TLS on port 465, otherwise plain SMTP — a lab relay only;
    /// plain SMTP is refused in Production.
    /// </summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>Sender mailbox, e.g. <c>"بوابة التوظيف" &lt;no-reply@example.gov.sy&gt;</c>.</summary>
    public string? From { get; set; }

    /// <summary>Optional SMTP user (no authentication when empty).</summary>
    public string? User { get; set; }

    /// <summary>Secret: set only through <c>Notifications__Candidate__Smtp__Password</c>.</summary>
    public string? Password { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary><c>Notifications:Candidate:Sms:*</c> (token: <c>Notifications__Candidate__Sms__AuthToken</c>, env/secret only).</summary>
public sealed class SmsChannelOptions
{
    /// <summary>Provider URL; the request is a POST with a JSON body. Keep secrets and phone numbers out of the URL.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Sender id / short code placed in <c>{sender}</c>.</summary>
    public string? SenderId { get; set; }

    /// <summary>Header that carries <see cref="AuthToken"/> (default <c>Authorization</c>; the token then includes its scheme).</summary>
    public string AuthHeaderName { get; set; } = "Authorization";

    /// <summary>Secret: set only through <c>Notifications__Candidate__Sms__AuthToken</c>.</summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// JSON body template. String values equal to or containing the placeholders <c>{to}</c>, <c>{sender}</c>,
    /// <c>{text}</c>, <c>{reference}</c> are replaced (values are JSON-encoded by the serializer, never spliced as raw text).
    /// </summary>
    public string BodyTemplate { get; set; } = DefaultBodyTemplate;

    public int TimeoutSeconds { get; set; } = 15;

    public const string DefaultBodyTemplate = """{"to":"{to}","from":"{sender}","text":"{text}","reference":"{reference}"}""";
}
