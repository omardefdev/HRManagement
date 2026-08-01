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
}
