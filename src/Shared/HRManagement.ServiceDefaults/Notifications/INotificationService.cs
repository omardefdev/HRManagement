using HRManagement.SharedKernel.Notifications;

namespace HRManagement.ServiceDefaults.Notifications;

/// <summary>COM-001: multi-channel notification service contract.</summary>
public interface INotificationService
{
    Task SendAsync(NotificationMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationMessage>> GetUnreadAsync(Guid tenantId, Guid recipientId, CancellationToken ct = default);
    Task MarkReadAsync(Guid notificationId, CancellationToken ct = default);
}
