using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Infrastructure.Integration;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HR.Core.Infrastructure.Services.Policy;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

public class AssetClassifierTests
{
    [Fact]
    public void ForClassifyFacts_omits_isPrivileged()
    {
        var asset = new Asset { Value = 20_000m, IsPrivileged = true };
        var facts = AssetPolicyFactsBuilder.ForClassifyFacts(asset);

        Assert.False(facts.ContainsKey("isPrivileged"));
        Assert.Equal("Classify", facts["action"]);
        Assert.Equal("20000", facts["value"]);
    }

    [Fact]
    public void PolicyStaticFallback_classify_high_value_sets_privileged()
    {
        var decision = PolicyStaticFallback.Evaluate(new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            Action = "Classify",
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = "15000",
                ["category"] = "",
                ["action"] = "Classify"
            }
        });

        Assert.True(decision.Allowed);
        Assert.Equal("true", decision.GetSetFieldValue("isPrivileged"));
    }

    [Fact]
    public void PolicyStaticFallback_classify_capital_category_sets_privileged()
    {
        var decision = PolicyStaticFallback.Evaluate(new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            Action = "Classify",
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = "6000",
                ["category"] = "أصول ثابتة",
                ["action"] = "Classify"
            }
        });

        Assert.Equal("true", decision.GetSetFieldValue("isPrivileged"));
    }

    [Fact]
    public void PolicyStaticFallbackShim_classify_low_value_not_privileged()
    {
        var decision = PolicyStaticFallbackShim.Evaluate(new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            Action = "Classify",
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = "100",
                ["category"] = "معدات",
                ["action"] = "Classify"
            }
        });

        Assert.Null(decision.GetSetFieldValue("isPrivileged"));
    }

    [Fact]
    public async Task ApplyClassificationAsync_sets_IsPrivileged_from_policy()
    {
        var policyClient = new Mock<IPolicyEnforceClient>();
        policyClient
            .Setup(c => c.EnforceAsync(It.IsAny<PolicyEnforceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyEnforceResponse
            {
                Allowed = true,
                Decision = new PolicyDecision
                {
                    Allowed = true,
                    EffectDetails =
                    [
                        new PolicyEffectDefinition
                        {
                            Effect = PolicyEffect.SetField,
                            Params = new Dictionary<string, string>
                            {
                                ["field"] = "isPrivileged",
                                ["value"] = "true"
                            }
                        }
                    ]
                }
            });

        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AdminLogisticsDbContext(options);
        var classifier = new AssetClassifier(db, policyClient.Object);
        var asset = new Asset { TagNumber = "T1", Name = "Test", Value = 12_000m };

        await classifier.ApplyClassificationAsync(asset);

        Assert.True(asset.IsPrivileged);
    }
}
