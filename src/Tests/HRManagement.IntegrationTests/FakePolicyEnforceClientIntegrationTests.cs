using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.TestSupport.Policy;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

public class FakePolicyEnforceClientIntegrationTests
{
    [Fact]
    public async Task Fake_client_can_be_registered_for_integration_fixtures()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.ReplacePolicyEnforceClientWithFake();
        var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<IPolicyEnforceClient>();
        var fake = sp.GetRequiredService<FakePolicyEnforceClient>();

        fake.EnforceHandler = _ => new PolicyEnforceResponse
        {
            Allowed = true,
            Decision = new PolicyDecision { Allowed = true },
            Explanations = ["integration-fake"]
        };

        var response = await client.EnforceAsync(new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            Action = "Assign",
            Mode = PolicyEnforcementMode.Enforce
        });

        Assert.True(response.Allowed);
        Assert.Single(fake.Requests);
    }
}
