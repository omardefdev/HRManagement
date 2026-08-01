using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Notifications;

/// <summary>COM-001: notification delivery channel.</summary>
public enum NotificationChannel
{
    InApp = 0,
    Email = 1,
    Sms = 2,
    Push = 3
}

/// <summary>COM-001: notification delivery status.</summary>
public enum NotificationStatus
{
    Pending = 0,
    Delivered = 1,
    Failed = 2,
    Expired = 3,
    Read = 4
}

/// <summary>
/// COM-001: durable notification message with delivery tracking.
/// Stored so that channel outage does not lose notifications.
/// </summary>
public class NotificationMessage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Target user/employee id.</summary>
    public Guid RecipientId { get; set; }

    /// <summary>Notification template code for localization.</summary>
    [Required]
    [MaxLength(100)]
    public string TemplateCode { get; set; } = string.Empty;

    /// <summary>Preferred delivery channel.</summary>
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;

    /// <summary>Rendered subject line (localized at send time).</summary>
    [MaxLength(500)]
    public string? Subject { get; set; }

    /// <summary>Rendered body (localized at send time).</summary>
    public string? Body { get; set; }

    /// <summary>Deep link into the application (relative URL).</summary>
    [MaxLength(500)]
    public string? ActionUrl { get; set; }

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    /// <summary>Idempotency key to prevent duplicate sends from the same event.</summary>
    [MaxLength(200)]
    public string? IdempotencyKey { get; set; }

    /// <summary>UTC expiration after which undelivered messages are marked Expired.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public int RetryCount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? DeliveredAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }
}
