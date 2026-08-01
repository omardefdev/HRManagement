using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Connectors;

/// <summary>
/// وصف موصل الاتحاد الحكومي (EXT-001) — لا تنفيذ Live في المستودع؛ يُستخدم للتحقق والسجل.
/// </summary>
public sealed class FederationConnectorDescriptor(IOptions<GovernmentConnectorOptions> options) : IGovernmentConnector
{
    private readonly ConnectorBindingOptions _binding = options.Value.Federation;

    public string ConnectorId => _binding.ConnectorId;

    public IntegrationConnectorMode Mode => _binding.Mode;

    public bool SandboxEnabled => _binding.Sandbox;

    public bool RequiredInProduction => _binding.RequiredInProduction;

    public string ExtCard => "EXT-001";

    public string Description =>
        "Government identity federation descriptor (national/agency IdP endpoints are external)";
}
