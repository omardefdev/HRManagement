using System.Text.Json;
using HRManagement.ServiceDefaults.Privacy;
using HRManagement.SharedKernel.Privacy;

namespace HRManagement.UnitTests;

public class Prv002DsarTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "PRV_002_INDIVIDUAL_RIGHTS.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Theory]
    [InlineData(DsarRequestType.Access, 30)]
    [InlineData(DsarRequestType.Correction, 30)]
    [InlineData(DsarRequestType.Restriction, 30)]
    [InlineData(DsarRequestType.Deletion, 45)]
    [InlineData(DsarRequestType.Objection, 30)]
    [InlineData(DsarRequestType.Appeal, 30)]
    public void Default_sla_days_match_workflow(DsarRequestType type, int expectedDays)
    {
        Assert.Equal(expectedDays, DsarWorkflowRules.DefaultSlaDays(type));
    }

    [Theory]
    [InlineData(DsarRequestStatus.Received, DsarRequestStatus.IdentityVerification, true)]
    [InlineData(DsarRequestStatus.Received, DsarRequestStatus.Rejected, true)]
    [InlineData(DsarRequestStatus.IdentityVerification, DsarRequestStatus.InProgress, true)]
    [InlineData(DsarRequestStatus.InProgress, DsarRequestStatus.PendingReview, true)]
    [InlineData(DsarRequestStatus.PendingReview, DsarRequestStatus.Fulfilled, true)]
    [InlineData(DsarRequestStatus.PendingReview, DsarRequestStatus.PartiallyFulfilled, true)]
    [InlineData(DsarRequestStatus.Rejected, DsarRequestStatus.AppealOpen, true)]
    [InlineData(DsarRequestStatus.AppealOpen, DsarRequestStatus.InProgress, true)]
    [InlineData(DsarRequestStatus.Received, DsarRequestStatus.Fulfilled, false)]
    [InlineData(DsarRequestStatus.Fulfilled, DsarRequestStatus.Received, false)]
    public void Workflow_transitions_match_prv002(DsarRequestStatus from, DsarRequestStatus to, bool allowed)
    {
        Assert.Equal(allowed, DsarWorkflowRules.IsAllowedTransition(from, to));
    }

    [Fact]
    public async Task DsarService_submit_sets_deadline_and_received_status()
    {
        var svc = new DsarService();
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();

        var entity = await svc.SubmitAsync(new DsarSubmitRequest(tenantId, subjectId, DsarRequestType.Access));

        Assert.Equal(DsarRequestStatus.Received, entity.Status);
        Assert.Equal(tenantId, entity.TenantId);
        Assert.Equal(subjectId, entity.SubjectId);
        Assert.Equal(
            entity.RequestedAtUtc.AddDays(DsarWorkflowRules.DefaultSlaDays(DsarRequestType.Access)).Date,
            entity.DeadlineUtc.Date);
    }

    [Fact]
    public async Task DsarService_happy_path_access_request()
    {
        var svc = new DsarService();
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();

        var created = await svc.SubmitAsync(new DsarSubmitRequest(tenantId, subjectId, DsarRequestType.Access));

        await svc.TransitionAsync(tenantId, created.Id, DsarRequestStatus.IdentityVerification);
        await svc.TransitionAsync(tenantId, created.Id, DsarRequestStatus.InProgress,
            new DsarTransitionOptions(DiscoveryScopeJson: "[\"HR.Core\",\"Payroll\"]"));
        await svc.TransitionAsync(tenantId, created.Id, DsarRequestStatus.PendingReview);
        var fulfilled = await svc.TransitionAsync(tenantId, created.Id, DsarRequestStatus.Fulfilled,
            new DsarTransitionOptions(DiscoveryPackageJson: "{\"masked\":true}"));
        var closed = await svc.TransitionAsync(tenantId, created.Id, DsarRequestStatus.Closed);

        Assert.Equal(DsarRequestStatus.Closed, closed.Status);
        Assert.NotNull(closed.CompletedAtUtc);
        Assert.NotNull(closed.DiscoveryPackageJson);
    }

    [Fact]
    public async Task DsarService_rejects_invalid_transition()
    {
        var svc = new DsarService();
        var tenantId = Guid.NewGuid();
        var created = await svc.SubmitAsync(new DsarSubmitRequest(tenantId, Guid.NewGuid(), DsarRequestType.Deletion));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.TransitionAsync(tenantId, created.Id, DsarRequestStatus.Fulfilled));
    }

    [Fact]
    public async Task DsarService_discover_sources_returns_ropa_aligned_services()
    {
        var svc = new DsarService();
        var sources = await svc.DiscoverSourcesAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Contains("HR.Core", sources);
        Assert.Contains("Payroll", sources);
        Assert.Contains("File.Storage", sources);
        Assert.True(sources.Count >= 10);
    }

    [Fact]
    public void Workflow_json_covers_all_request_types()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "prv", "dsar-workflow.json")));
        var root = doc.RootElement;
        Assert.Equal("PRV-002", root.GetProperty("card").GetString());

        var types = root.GetProperty("requestTypes").EnumerateArray()
            .Select(t => t.GetProperty("type").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var need in new[] { "Access", "Correction", "Restriction", "Deletion", "Objection", "Appeal" })
            Assert.Contains(need, types);

        Assert.True(root.GetProperty("noDirectDbEdit").GetBoolean());
        Assert.True(root.GetProperty("appealIndependent").GetBoolean());
    }

    [Fact]
    public void Prv002_docs_and_scripts_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "PRV_002_INDIVIDUAL_RIGHTS.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "prv", "dsar-workflow.json")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "prv002-validate.ps1")));
    }
}
