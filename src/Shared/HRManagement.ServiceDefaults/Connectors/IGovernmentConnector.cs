namespace HRManagement.ServiceDefaults.Connectors;

/// <summary>وصف موصل حكومي مسجّل (INT-004). نقاط النهاية الوطنية خارج نطاق المستودع.</summary>
public interface IGovernmentConnector
{
    string ConnectorId { get; }

    IntegrationConnectorMode Mode { get; }

    /// <summary>عند Live=true و Sandbox=true تُرجَع استجابات sandbox معلّمة؛ Live+!Sandbox يرمي NotSupported حتى ربط EXT.</summary>
    bool SandboxEnabled { get; }

    bool RequiredInProduction { get; }

    string ExtCard { get; }

    string Description { get; }
}
