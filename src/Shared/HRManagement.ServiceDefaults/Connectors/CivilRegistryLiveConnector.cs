using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Identity;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Connectors;

/// <summary>
/// موصل السجل المدني Live (INT-004). نقاط النهاية الوطنية خارج المستودع — EXT-002a–d.
/// Live+Sandbox: استجابة sandbox معلّمة؛ Live+!Sandbox: NotSupportedException.
/// </summary>
public sealed class CivilRegistryLiveConnector(IOptions<GovernmentConnectorOptions> options) :
    IGovernmentConnector,
    INationalIdentityVerifier
{
    private readonly ConnectorBindingOptions _binding = options.Value.CivilRegistry;

    public string ConnectorId => _binding.ConnectorId;

    public IntegrationConnectorMode Mode => _binding.Mode;

    public bool SandboxEnabled => _binding.Sandbox;

    public bool RequiredInProduction => _binding.RequiredInProduction;

    public string ExtCard => "EXT-002";

    public string Description =>
        "MOI civil registry identity proof live connector (national endpoints are external)";

    public Task<IntegrationResult<NationalIdentityVerificationResult>> VerifyAsync(
        NationalIdentityVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Mode is IntegrationConnectorMode.File or IntegrationConnectorMode.Stub)
        {
            return Task.FromResult(IntegrationResult<NationalIdentityVerificationResult>.Fail(
                "CivilRegistryLiveConnector requires Live or Sandbox mode"));
        }

        if (Mode == IntegrationConnectorMode.Sandbox || SandboxEnabled)
        {
            var result = new NationalIdentityVerificationResult(
                IsValid: true,
                FullName: request.FullName ?? "SANDBOX-NAME",
                StatusMessage: "INT-004 sandbox civil registry ack (national endpoint external)");

            return Task.FromResult(IntegrationResult<NationalIdentityVerificationResult>.Ok(
                result,
                message: "INT-004 sandbox civil registry ack (national endpoint external)",
                externalReference: $"SANDBOX-CIVIL-{request.NationalId}"));
        }

        throw new NotSupportedException(
            "Live civil registry national endpoints are external; enable Sandbox or complete EXT-002a–d certification.");
    }
}
