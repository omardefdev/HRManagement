using System.Text.Json;
using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Services;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Domain.Services;

namespace HRManagement.UnitTests;

public class Fun006JourneyMatrixTests
{
    private static string ResolveMatrixPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "fun-archive", "journey-matrix.json");
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("docs/fun-archive/journey-matrix.json");
    }

    [Fact]
    public void Matrix_includes_archive_analytics_report()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ResolveMatrixPath()));
        var ids = doc.RootElement.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ArchiveLifecycle", ids);
        Assert.Contains("AnalyticsProjection", ids);
        Assert.Contains("BranchReportExport", ids);
    }
}

public class Fun006ArchiveGovTests
{
    [Fact]
    public void Hold_blocks_dispose_and_request()
    {
        var item = new ArchiveItem
        {
            CreatedAt = DateTime.UtcNow.AddYears(-10),
            RetentionPeriodYears = 1,
            IsOnLegalHold = true,
            DisposeApprovedAt = DateTime.UtcNow
        };
        Assert.False(ArchiveRetentionRules.CanDispose(item, false));
        Assert.False(ArchiveRetentionRules.CanRequestDispose(item, false));
    }

    [Fact]
    public void Sod_blocks_same_approver()
    {
        var requester = Guid.NewGuid();
        var item = new ArchiveItem
        {
            CreatedAt = DateTime.UtcNow.AddYears(-10),
            RetentionPeriodYears = 1,
            DisposeRequestedAt = DateTime.UtcNow,
            DisposeRequestedBy = requester
        };
        Assert.False(ArchiveRetentionRules.CanApproveDispose(item, requester, requester));
        Assert.True(ArchiveRetentionRules.CanApproveDispose(item, requester, Guid.NewGuid()));
    }

    [Fact]
    public void Checksum_and_completeness()
    {
        Assert.False(ArchiveRetentionRules.HasChecksum("abc"));
        Assert.True(ArchiveRetentionRules.HasChecksum("deadbeef01"));
        var files = new[]
        {
            new DigitalFileRecord { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Checksum = "aaaaaaaa" },
            new DigitalFileRecord { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Checksum = "bbbbbbbb" }
        };
        var fp = ArchiveRetentionRules.CompletenessFingerprint(files);
        Assert.Contains("aaaaaaaa", fp);
        Assert.Equal(fp, ArchiveRetentionRules.CompletenessFingerprint(files.Reverse()));
    }

    [Fact]
    public void Checklist_defaults_cover_dispose()
    {
        var steps = ArchiveChecklistDefaults.CreateFor(Guid.NewGuid()).Select(s => s.Step).ToHashSet();
        Assert.Contains(ArchiveChecklistStep.DisposeApproved, steps);
        Assert.Contains(ArchiveChecklistStep.Disposed, steps);
    }
}

public class Fun006AnalyticsProjectionTests
{
    [Fact]
    public void Rebuild_fingerprint_is_deterministic()
    {
        var org = Guid.NewGuid();
        var id1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var id2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            new AnalyticsEvent { Id = id2, EventName = "EmployeeCreated", OccurredAt = t.AddHours(1), OrgUnitId = org },
            new AnalyticsEvent { Id = id1, EventName = "EmployeeCreated", OccurredAt = t, OrgUnitId = org }
        };
        var a = AnalyticsProjectionRules.Fingerprint(events);
        var b = AnalyticsProjectionRules.Fingerprint(events.Reverse());
        Assert.Equal(a, b);

        var proj1 = AnalyticsProjectionRules.BuildHeadcountProjection(events, org);
        var proj2 = AnalyticsProjectionRules.BuildHeadcountProjection(events, org);
        Assert.Equal(proj1, proj2);
    }

    [Fact]
    public void Branch_scope_forbids_tenant_wide()
    {
        Assert.False(BranchReportScopeRules.AllowsTenantWide(isBranchScoped: true));
        Assert.True(BranchReportScopeRules.AllowsTenantWide(isBranchScoped: false));
        var root = Guid.NewGuid();
        Assert.True(BranchReportScopeRules.OrgMatchesBranch(root, root));
        Assert.False(BranchReportScopeRules.OrgMatchesBranch(Guid.NewGuid(), root));
    }

    [Fact]
    public void Org_filter_changes_headcount()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var events = new[]
        {
            new AnalyticsEvent { Id = Guid.NewGuid(), EventName = "EmployeeCreated", OccurredAt = DateTime.UtcNow, OrgUnitId = orgA },
            new AnalyticsEvent { Id = Guid.NewGuid(), EventName = "EmployeeCreated", OccurredAt = DateTime.UtcNow, OrgUnitId = orgB },
            new AnalyticsEvent { Id = Guid.NewGuid(), EventName = "SeparationCompleted", OccurredAt = DateTime.UtcNow, OrgUnitId = orgA }
        };
        using var docA = JsonDocument.Parse(AnalyticsProjectionRules.BuildHeadcountProjection(events, orgA));
        using var docB = JsonDocument.Parse(AnalyticsProjectionRules.BuildHeadcountProjection(events, orgB));
        Assert.Equal(0, docA.RootElement.GetProperty("active").GetInt32());
        Assert.Equal(1, docB.RootElement.GetProperty("active").GetInt32());
    }
}
