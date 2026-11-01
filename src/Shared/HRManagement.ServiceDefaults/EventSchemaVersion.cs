using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

public static class EventSchemaVersion
{
    public static bool Supports(int messageVersion, int supportedMax, ILogger logger, string eventName)
    {
        if (messageVersion <= supportedMax)
            return true;

        logger.LogWarning(
            "Skipping {EventName}: SchemaVersion {MessageVersion} exceeds supported max {SupportedMax}",
            eventName, messageVersion, supportedMax);
        return false;
    }

    /// <summary>
    /// PLAN-37 (G-COD-06): for consumers that must never acknowledge a message without its effect (the archive). A newer
    /// schema throws <see cref="UnsupportedEventSchemaException"/>, an <see cref="InvalidOperationException"/>, which the
    /// bus policy does not retry: the message goes straight to <c>_error</c> for a replay once the consumer is upgraded.
    /// </summary>
    public static void Require(int messageVersion, int supportedMax, string eventName)
    {
        if (messageVersion > supportedMax)
            throw new UnsupportedEventSchemaException(eventName, messageVersion, supportedMax);
    }
}

public sealed class UnsupportedEventSchemaException(string eventName, int messageVersion, int supportedMax)
    : InvalidOperationException($"{eventName}: SchemaVersion {messageVersion} exceeds supported max {supportedMax}; the message is dead-lettered, not dropped.")
{
    public string EventName { get; } = eventName;
}
