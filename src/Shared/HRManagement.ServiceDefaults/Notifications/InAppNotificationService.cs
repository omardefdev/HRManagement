using System.Collections.Concurrent;
using HRManagement.SharedKernel.Notifications;

namespace HRManagement.ServiceDefaults.Notifications;

/// <summary>
/// COM-001: in-memory in-app notification service for development and testing.
/// Production replaces with persistent + SignalR implementation.
/// </summary>
public sealed class InAppNotificationService : INotificationService
{
    private readonly ConcurrentDictionary<Guid, NotificationMessage> _store = new();

    public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!string.IsNullOrEmpty(message.IdempotencyKey))
        {
            var duplicate = _store.Values.FirstOrDefault(m =>
                m.IdempotencyKey == message.IdempotencyKey
                && m.TenantId == message.TenantId);
            if (duplicate is not null)
                return Task.CompletedTask;
        }

        message.Status = NotificationStatus.Delivered;
        message.DeliveredAtUtc = DateTime.UtcNow;
        _store[message.Id] = message;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NotificationMessage>> GetUnreadAsync(
        Guid tenantId, Guid recipientId, CancellationToken ct = default)
    {
        var unread = _store.Values
            .Where(m => m.TenantId == tenantId
                     && m.RecipientId == recipientId
                     && m.Status == NotificationStatus.Delivered)
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<NotificationMessage>>(unread);
    }

    public Task MarkReadAsync(Guid notificationId, CancellationToken ct = default)
    {
        if (_store.TryGetValue(notificationId, out var msg))
        {
            msg.Status = NotificationStatus.Read;
            msg.ReadAtUtc = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }
}
