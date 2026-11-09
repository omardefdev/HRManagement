using System.Security.Claims;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-47 §6 (D-POL-10): a version takes effect at a set time; evaluation runs as of a date (in HR.Core, never from the internal
/// endpoint); the ledger records the as-of time; the "now" cache never outlives the next EffectiveFrom.
/// </summary>
public sealed class Pol047EffectiveDatingTests
{
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    private static PolicyOrchestrator Orchestrator(Pol047Harness h, IPolicyPublishedSnapshotCache? cache = null) =>
        new(h.Db, Mock.Of<IWorkflowStarter>(), cache ?? h.Cache, NullLogger<PolicyOrchestrator>.Instance);

    private static PolicyEnforceRequest Request(DateTime? asOf = null) => new()
    {
        Domain = Pol047Harness.Domain,
        Action = "Assign",
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["category"] = "أصول ثابتة", ["isPrivileged"] = "false" },
        Mode = PolicyEnforcementMode.Enforce,
        AsOfUtc = asOf
    };

    private static async Task<(Pol047Harness H, PolicyTemplateVersion V1, Guid V2)> FutureVersionAsync(TimeSpan inFuture)
    {
        var h = new Pol047Harness();
        var (template, v1) = await h.SeedTemplateAsync();
        var v2 = await h.DraftByAsync(template.Id, Author);
        var at = DateTime.UtcNow.Add(inFuture);
        Assert.True((await h.Drafts.SetEffectiveFromAsync(v2, at, Author)).Succeeded);
        var published = await h.PublishService.PublishAsync(new(v2, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), null));
        Assert.True(published.Succeeded, published.MessageAr);
        return (h, v1, v2);
    }

    [Fact]
    public async Task A_later_version_takes_effect_at_its_time_and_evaluation_runs_as_of_a_date()
    {
        var (h, v1, v2) = await FutureVersionAsync(TimeSpan.FromMinutes(10));
        await using (h)
        {
            var orchestrator = Orchestrator(h);

            var now = await orchestrator.EvaluateAsync(Request());
            var later = await orchestrator.EvaluateAsync(Request(DateTime.UtcNow.AddMinutes(11)));
            var earlier = await orchestrator.EvaluateAsync(Request(DateTime.UtcNow.AddDays(-10)));

            Assert.Equal(v1.Id, now.PolicyVersionId);
            Assert.Equal(v2, later.PolicyVersionId);
            Assert.Equal(v1.Id, earlier.PolicyVersionId);
            Assert.Contains("R-NEW", later.MatchedRuleIds);
            Assert.DoesNotContain("R-NEW", now.MatchedRuleIds);

            // v1 stays effective until v2 starts: only its EffectiveTo changed.
            var closed = await h.Db.PolicyTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == v1.Id);
            var started = await h.Db.PolicyTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == v2);
            Assert.Equal(started.EffectiveFrom, closed.EffectiveTo);
            Assert.Equal(PolicyVersionStatus.Published, closed.Status);
        }
    }

    [Fact]
    public async Task The_now_cache_lives_until_the_next_effective_time_at_most()
    {
        var (h, v1, _) = await FutureVersionAsync(TimeSpan.FromMinutes(2));
        await using (h)
        {
            var spy = new SpyCache();
            var snapshot = await Orchestrator(h, spy).GetPublishedSnapshotAsync(Pol047Harness.Domain, null);

            Assert.Equal(v1.Id, snapshot!.VersionId);
            var (key, ttl) = Assert.Single(spy.Sets);
            Assert.Equal(IPolicyPublishedSnapshotCache.BuildCacheKey(Pol047Harness.Domain, null), key);
            Assert.InRange(ttl, TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(2));

            // An evaluation as of a date is never cached.
            await Orchestrator(h, spy).GetPublishedSnapshotAsync(Pol047Harness.Domain, null, DateTime.UtcNow.AddMinutes(3));
            Assert.Single(spy.Sets);
        }

        Assert.Equal(PolicyPublishedSnapshotCache.DefaultTtl, PolicyPublishedSnapshotCache.TimeToLive(DateTime.UtcNow, null));
        Assert.Equal(PolicyPublishedSnapshotCache.DefaultTtl, PolicyPublishedSnapshotCache.TimeToLive(DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
        var now = DateTime.UtcNow;
        Assert.Equal(TimeSpan.FromSeconds(30), PolicyPublishedSnapshotCache.TimeToLive(now, now.AddSeconds(30)));
    }

    [Fact]
    public async Task A_retroactive_effective_time_is_refused_unless_the_setting_opens_it()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var draft = await h.DraftByAsync(template.Id, Author);

        var refused = await h.Drafts.SetEffectiveFromAsync(draft, DateTime.UtcNow.AddDays(-1), Author);
        Assert.Equal(PolicyDraftOutcome.Invalid, refused.Outcome);
        Assert.Contains(PolicyPublishService.RetroactiveMessage, refused.MessageAr);
        Assert.True((await h.Drafts.SetEffectiveFromAsync(draft, DateTime.UtcNow.AddMinutes(-4), Author)).Succeeded);

        await using var open = new Pol047Harness(new() { [PolicyPublishService.AllowRetroactiveKey] = "true" });
        var (t2, _) = await open.SeedTemplateAsync();
        var d2 = await open.DraftByAsync(t2.Id, Author);
        Assert.True((await open.Drafts.SetEffectiveFromAsync(d2, DateTime.UtcNow.AddDays(-1), Author)).Succeeded);
        Assert.True((await open.PublishService.PublishAsync(new(d2, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), null))).Succeeded);
    }

    [Fact]
    public async Task A_draft_dated_in_the_past_by_any_path_is_refused_at_publish()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Author);
        var draft = await h.Db.PolicyTemplateVersions.SingleAsync(v => v.Id == draftId);
        draft.EffectiveFrom = DateTime.UtcNow.AddDays(-2);
        await h.Db.SaveChangesAsync();

        var result = await h.PublishService.PublishAsync(new(draftId, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), null));

        Assert.Equal(PolicyPublishOutcome.Invalid, result.Outcome);
        Assert.Contains(result.IssueList, i => i.JsonPath == "$.effectiveFrom");
    }

    [Fact]
    public async Task The_ledger_records_the_as_of_time_of_a_decision()
    {
        var (h, _, v2) = await FutureVersionAsync(TimeSpan.FromMinutes(10));
        await using (h)
        {
            var asOf = DateTime.UtcNow.AddMinutes(11);
            var dated = await Orchestrator(h).EnforceAsync(Request(asOf));
            var current = await Orchestrator(h).EnforceAsync(Request());

            Assert.Equal(v2, dated.PolicyVersionId);
            var datedRow = await h.Db.PolicyEnforcementLedgers.AsNoTracking().SingleAsync(l => l.Id == dated.LedgerId);
            var currentRow = await h.Db.PolicyEnforcementLedgers.AsNoTracking().SingleAsync(l => l.Id == current.LedgerId);
            Assert.Equal(asOf, datedRow.EvaluatedAsOf!.Value, TimeSpan.FromMilliseconds(1));
            Assert.Null(currentRow.EvaluatedAsOf);
        }
    }

    [Fact]
    public async Task The_internal_endpoint_ignores_an_as_of_time_in_the_body()
    {
        var orchestrator = new Mock<IPolicyOrchestrator>();
        PolicyEnforceRequest? seen = null;
        orchestrator.Setup(o => o.EnforceForServiceAsync(It.IsAny<PolicyEnforceRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<PolicyEnforceRequest, string, CancellationToken>((r, _, _) => seen = r)
            .ReturnsAsync(new PolicyEnforceResponse { Allowed = true, EffectiveMode = PolicyEnforcementMode.Enforce });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"PolicyEngine:{PolicyDomains.AssetsCustody}"] = "Enforce" })
            .Build();
        var tenant = Guid.NewGuid();
        var controller = new PolicyEnforceInternalController(orchestrator.Object, new PolicyModeResolver(config), Mock.Of<ITenantContext>(t => t.TenantId == tenant))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", "svc-admin-logistics")], "Bearer")) }
            }
        };

        var result = await controller.Enforce(new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            SubjectType = "AssetAssignment",
            Action = "Assign",
            AsOfUtc = DateTime.UtcNow.AddYears(-3)
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(seen!.AsOfUtc);
    }

    private sealed class SpyCache : IPolicyPublishedSnapshotCache
    {
        public List<(string Key, TimeSpan Ttl)> Sets { get; } = [];

        public bool TryGet(string cacheKey, out PolicyVersionSnapshot? snapshot)
        {
            snapshot = null;
            return false;
        }

        public void Set(string cacheKey, PolicyVersionSnapshot snapshot, TimeSpan ttl) => Sets.Add((cacheKey, ttl));

        public void InvalidateDomain(string domain) { }
    }
}
