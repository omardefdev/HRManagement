namespace HRManagement.ServiceDefaults.Connectors;

/// <summary>وضع تشغيل موصل حكومي: ملف، محاكاة، sandbox، أو ربط حي (INT-004).</summary>
public enum IntegrationConnectorMode
{
    File = 0,
    Stub = 1,
    Sandbox = 2,
    Live = 3
}
