namespace HRManagement.ServiceDefaults;

public interface IIntegrationMessageInbox
{
    Task<bool> HasProcessedAsync(string messageId, string consumerType, CancellationToken cancellationToken = default);

    Task TryRecordAsync(string messageId, string consumerType, CancellationToken cancellationToken = default);
}
