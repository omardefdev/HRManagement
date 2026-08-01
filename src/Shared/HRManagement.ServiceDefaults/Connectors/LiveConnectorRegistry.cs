namespace HRManagement.ServiceDefaults.Connectors;

public sealed record GovernmentConnectorDescriptor(
    string ConnectorId,
    string ExtCard,
    string Description,
    bool SandboxAvailable,
    bool RequiredInProduction);

public sealed class LiveConnectorRegistry
{
    public static IReadOnlyList<GovernmentConnectorDescriptor> BuiltIn { get; } =
    [
        new(
            ConnectorId: "treasury",
            ExtCard: "EXT-003",
            Description: "Treasury payroll disbursement channel (national endpoints external)",
            SandboxAvailable: true,
            RequiredInProduction: true),
        new(
            ConnectorId: "civil-registry",
            ExtCard: "EXT-002",
            Description: "MOI civil registry identity proof (national endpoints external)",
            SandboxAvailable: true,
            RequiredInProduction: true),
        new(
            ConnectorId: "federation",
            ExtCard: "EXT-001",
            Description: "Government identity federation assurance (national IdP external)",
            SandboxAvailable: true,
            RequiredInProduction: false)
    ];

    private readonly Dictionary<string, IGovernmentConnector> _connectors = new(StringComparer.OrdinalIgnoreCase);

    public LiveConnectorRegistry(IEnumerable<IGovernmentConnector> connectors)
    {
        foreach (var c in connectors)
            _connectors[c.ConnectorId] = c;
    }

    public IReadOnlyCollection<IGovernmentConnector> All => _connectors.Values.ToList();

    public IGovernmentConnector? Get(string connectorId) =>
        _connectors.TryGetValue(connectorId, out var c) ? c : null;

    public GovernmentConnectorDescriptor? GetDescriptor(string connectorId) =>
        BuiltIn.FirstOrDefault(d => string.Equals(d.ConnectorId, connectorId, StringComparison.OrdinalIgnoreCase));
}
