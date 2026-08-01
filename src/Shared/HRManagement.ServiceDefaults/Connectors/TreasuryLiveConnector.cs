using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Payroll;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Connectors;

/// <summary>
/// موصل خزينة Live (INT-004). نقاط النهاية الوطنية خارج المستودع — EXT-003a–e.
/// Live+Sandbox: استجابة sandbox معلّمة؛ Live+!Sandbox: NotSupportedException.
/// </summary>
public sealed class TreasuryLiveConnector(IOptions<GovernmentConnectorOptions> options) :
    IGovernmentConnector,
    IPayrollDisbursementGateway
{
    private readonly ConnectorBindingOptions _binding = options.Value.Treasury;

    public string ConnectorId => _binding.ConnectorId;

    public IntegrationConnectorMode Mode => _binding.Mode;

    public bool SandboxEnabled => _binding.Sandbox;

    public bool RequiredInProduction => _binding.RequiredInProduction;

    public string ExtCard => "EXT-003";

    public string Description =>
        "Treasury payroll disbursement live connector (national treasury endpoints are external)";

    public Task<IntegrationResult> SubmitAsync(
        PayrollDisbursementRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Mode is IntegrationConnectorMode.File or IntegrationConnectorMode.Stub)
            return Task.FromResult(IntegrationResult.Fail("TreasuryLiveConnector requires Live or Sandbox mode"));

        if (Mode == IntegrationConnectorMode.Sandbox || SandboxEnabled)
        {
            return Task.FromResult(new IntegrationResult(
                Success: true,
                Message: "INT-004 sandbox treasury ack (national endpoint external)",
                ExternalReference: $"SANDBOX-TREASURY-{request.PayrollRunId}",
                Metadata: new Dictionary<string, string>
                {
                    ["connector"] = ConnectorId,
                    ["sandbox"] = "true",
                    ["extCard"] = ExtCard
                }));
        }

        throw new NotSupportedException(
            "Live treasury national endpoints are external; enable Sandbox or complete EXT-003a–e certification.");
    }
}
