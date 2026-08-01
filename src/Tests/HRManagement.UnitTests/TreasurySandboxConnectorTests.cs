using HRManagement.Integration.Abstractions.Payroll;
using HRManagement.ServiceDefaults.Connectors;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

public class TreasurySandboxConnectorTests
{
    [Fact]
    public async Task Sandbox_mode_returns_ack_with_external_reference()
    {
        var connector = new TreasuryLiveConnector(Options.Create(new GovernmentConnectorOptions
        {
            Treasury = new ConnectorBindingOptions
            {
                ConnectorId = "treasury",
                Mode = IntegrationConnectorMode.Sandbox,
                Sandbox = true
            }
        }));

        var runId = Guid.NewGuid();
        var request = new PayrollDisbursementRequest(
            runId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            "SYP",
            [new PayrollDisbursementLine(Guid.NewGuid(), null, "Test", 600m, 100m, 500m)]);

        var result = await connector.SubmitAsync(request);

        Assert.True(result.Success);
        Assert.Contains("SANDBOX-TREASURY", result.ExternalReference, StringComparison.Ordinal);
        Assert.Equal("true", result.Metadata?["sandbox"]);
    }

    [Fact]
    public async Task Live_without_sandbox_throws_not_supported()
    {
        var connector = new TreasuryLiveConnector(Options.Create(new GovernmentConnectorOptions
        {
            Treasury = new ConnectorBindingOptions
            {
                ConnectorId = "treasury",
                Mode = IntegrationConnectorMode.Live,
                Sandbox = false
            }
        }));

        await Assert.ThrowsAsync<NotSupportedException>(() => connector.SubmitAsync(new PayrollDisbursementRequest(
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            "SYP",
            [])));
    }
}
