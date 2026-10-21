using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

public class PolicyOrchestratorPublishTests
{
    private static readonly Guid TenantId = AcceptanceSeedIds.AgencyAId;

    private static (HRCoreDbContext Db, IPolicyOrchestrator Orchestrator, IPolicyPublishedSnapshotCache Cache) CreateHarness(string dbName)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(TenantId);
        tenant.Setup(t => t.IsPlatformOperator).Returns(false);

        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var db = new HRCoreDbContext(options, tenant.Object);
        var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new PolicyPublishedSnapshotCache(memory);
        var workflow = new Mock<IWorkflowEngine>();
        var orchestrator = new PolicyOrchestrator(db, Mock.Of<IWorkflowStarter>(), cache, NullLogger<PolicyOrchestrator>.Instance);
        var publisher = new PolicyVersionPublisher(db, cache);
        return (db, orchestrator, cache);
    }

    [Fact]
    public async Task Publish_invalidates_snapshot_cache_immediately()
    {
        var dbName = $"policy-cache-{Guid.NewGuid():N}";
        var (db, orchestrator, cache) = CreateHarness(dbName);
        await using (db)
        {
            var template = new PolicyTemplate
            {
                TenantId = TenantId,
                Domain = PolicyDomains.AssetsCustody,
                Name = "Test",
                SubjectType = "Asset",
                Scope = PolicyTemplateScope.TenantOverride
            };
            db.PolicyTemplates.Add(template);

            var v1 = new PolicyTemplateVersion
            {
                TenantId = TenantId,
                PolicyTemplate = template,
                VersionNumber = 1,
                Status = PolicyVersionStatus.Published,
                EffectiveFrom = DateTime.UtcNow.AddDays(-10)
            };
            db.PolicyRules.Add(new PolicyRule
            {
                TenantId = TenantId,
                PolicyTemplateVersion = v1,
                RuleKey = "V1",
                WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""",
                ThenJson = """[{"effect":"RequireEvidence"}]"""
            });

            var v2Draft = new PolicyTemplateVersion
            {
                TenantId = TenantId,
                PolicyTemplate = template,
                VersionNumber = 2,
                Status = PolicyVersionStatus.Draft
            };
            db.PolicyRules.Add(new PolicyRule
            {
                TenantId = TenantId,
                PolicyTemplateVersion = v2Draft,
                RuleKey = "V2",
                WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""",
                ThenJson = """[{"effect":"Deny","params":{"reason":"blocked"}}]"""
            });
            db.PolicyTemplateVersions.AddRange(v1, v2Draft);
            await db.SaveChangesAsync();

            var before = await orchestrator.GetPublishedSnapshotAsync(PolicyDomains.AssetsCustody, TenantId);
            Assert.NotNull(before);
            Assert.Equal(v1.Id, before!.VersionId);

            var publisher = new PolicyVersionPublisher(db, cache);
            v2Draft.EffectiveFrom = DateTime.UtcNow;
            await publisher.PublishDraftAsync(v2Draft, Guid.NewGuid(), Guid.NewGuid(), "unit-test");

            var after = await orchestrator.GetPublishedSnapshotAsync(PolicyDomains.AssetsCustody, TenantId);
            Assert.NotNull(after);
            Assert.Equal(v2Draft.Id, after!.VersionId);
        }
    }

    [Fact]
    public async Task Publish_closes_previous_EffectiveTo_and_asOf_selects_correct_version()
    {
        var dbName = $"policy-effective-{Guid.NewGuid():N}";
        var (db, orchestrator, cache) = CreateHarness(dbName);
        await using (db)
        {
            var template = new PolicyTemplate
            {
                TenantId = TenantId,
                Domain = PolicyDomains.AssetsCustody,
                Name = "EffectiveTo",
                SubjectType = "Asset",
                Scope = PolicyTemplateScope.TenantOverride
            };
            db.PolicyTemplates.Add(template);

            var v1 = new PolicyTemplateVersion
            {
                TenantId = TenantId,
                PolicyTemplate = template,
                VersionNumber = 1,
                Status = PolicyVersionStatus.Published,
                EffectiveFrom = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            db.PolicyRules.Add(new PolicyRule
            {
                TenantId = TenantId,
                PolicyTemplateVersion = v1,
                RuleKey = "V1",
                WhenJson = "{}",
                ThenJson = "[]"
            });

            var v2Draft = new PolicyTemplateVersion
            {
                TenantId = TenantId,
                PolicyTemplate = template,
                VersionNumber = 2,
                Status = PolicyVersionStatus.Draft,
                EffectiveFrom = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            db.PolicyRules.Add(new PolicyRule
            {
                TenantId = TenantId,
                PolicyTemplateVersion = v2Draft,
                RuleKey = "V2",
                WhenJson = "{}",
                ThenJson = "[]"
            });
            db.PolicyTemplateVersions.AddRange(v1, v2Draft);
            await db.SaveChangesAsync();

            var publisher = new PolicyVersionPublisher(db, cache);
            await publisher.PublishDraftAsync(v2Draft, Guid.NewGuid(), Guid.NewGuid(), "effective-to-test");

            var reloadedV1 = await db.PolicyTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == v1.Id);
            Assert.Equal(v2Draft.EffectiveFrom, reloadedV1.EffectiveTo);

            var asOfV1Window = await orchestrator.GetPublishedSnapshotAsync(
                PolicyDomains.AssetsCustody, TenantId, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.NotNull(asOfV1Window);
            Assert.Equal(v1.Id, asOfV1Window!.VersionId);

            var asOfCurrent = await orchestrator.GetPublishedSnapshotAsync(
                PolicyDomains.AssetsCustody, TenantId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.NotNull(asOfCurrent);
            Assert.Equal(v2Draft.Id, asOfCurrent!.VersionId);
        }
    }
}

public class FakePolicyEnforceClientTests
{
    [Fact]
    public async Task Fake_client_records_requests_and_returns_configured_response()
    {
        var fake = new HRManagement.TestSupport.Policy.FakePolicyEnforceClient
        {
            EnforceHandler = req => new PolicyEnforceResponse
            {
                Allowed = false,
                Decision = new PolicyDecision { Allowed = false, DenyReason = "test-deny" },
                Explanations = ["injected"]
            }
        };

        var request = new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            Action = "Assign",
            Mode = PolicyEnforcementMode.Enforce,
            Facts = new Dictionary<string, string> { ["isPrivileged"] = "true" }
        };

        var response = await fake.EnforceAsync(request);
        Assert.False(response.Allowed);
        Assert.Single(fake.Requests);
        Assert.Equal("Assign", fake.Requests[0].Action);
    }
}
