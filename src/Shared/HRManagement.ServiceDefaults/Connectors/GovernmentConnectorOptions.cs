namespace HRManagement.ServiceDefaults.Connectors;

public sealed class GovernmentConnectorOptions
{
    public const string SectionName = "GovernmentConnectors";

    public bool EnforceOnStartup { get; set; } = true;

    public ConnectorBindingOptions Treasury { get; set; } = new() { ConnectorId = "treasury" };

    public ConnectorBindingOptions CivilRegistry { get; set; } = new() { ConnectorId = "civil-registry" };

    public ConnectorBindingOptions Federation { get; set; } = new()
    {
        ConnectorId = "federation",
        RequiredInProduction = false
    };
}

public sealed class ConnectorBindingOptions
{
    public string ConnectorId { get; set; } = "";

    public IntegrationConnectorMode Mode { get; set; } = IntegrationConnectorMode.File;

    public bool Sandbox { get; set; } = true;

    public bool RequiredInProduction { get; set; } = true;
}
