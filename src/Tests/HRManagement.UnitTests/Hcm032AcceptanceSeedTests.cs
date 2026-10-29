using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Analytics;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-032 (PLAN-32, P7): the AC-HCM-32 fixtures (<see cref="AcceptanceRecruitmentReportsPatch"/>) are seeded once and
/// give the hand-computed values the acceptance test expects (funnel 6,4,3,3,3,3,2,1,1; time to fill 37 days; branch
/// scope; one overdue approval).
/// </summary>
public sealed class Hcm032AcceptanceSeedTests
{
    private static readonly RecruitmentMetricsFilter Period = new() { From = new DateOnly(2026, 8, 1), To = new DateOnly(2026, 10, 1) };

    [Fact]
    public async Task Recruitment_reports_patch_seeds_the_ac_hcm_32_fixtures_once()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var seed = new HRCoreDbContext(options))
        {
            await AcceptanceDataSeeder.SeedAsync(seed, NullLogger.Instance);
            await WorkflowPackSeeder.SeedAsync(seed);
            await AcceptanceRecruitmentReportsPatch.ApplyAsync(seed, NullLogger.Instance);
            await AcceptanceRecruitmentReportsPatch.ApplyAsync(seed, NullLogger.Instance);
        }

        await using var db = new HRCoreDbContext(options);
        var requisitions = await db.RecruitmentRequisitions.IgnoreQueryFilters()
            .Where(r => r.RequisitionNumber.StartsWith("REQ-2026-A-AC32-")).OrderBy(r => r.RequisitionNumber).ToListAsync();
        Assert.Equal([RequisitionStatus.Closed, RequisitionStatus.Published, RequisitionStatus.PendingApproval], requisitions.Select(r => r.Status));
        Assert.All(requisitions, r => Assert.Equal(AcceptanceSeedIds.AgencyAId, r.TenantId));
        Assert.Equal(9, await db.RecruitmentApplications.IgnoreQueryFilters()
            .CountAsync(a => a.ApplicantFullName.StartsWith(AcceptanceSeedIds.RecruitmentReportsApplicantPrefix)));

        var service = new RecruitmentMetricsService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()),
            new Hcm032MetricsFixture.FixedTimeProvider(new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc)));
        var unrestricted = OrgAccessScope.Unrestricted();

        var detail = await service.GetRequisitionDetailAsync(AcceptanceSeedIds.RecruitmentReportsRequisition1Id, unrestricted, includeNames: false);
        Assert.NotNull(detail);
        Assert.Equal([6, 4, 3, 3, 3, 3, 2, 1, 1], detail!.Funnel.Stages.Select(s => s.Count));
        Assert.Equal(37, detail.Requisition.TimeToFillDays);
        var fill = await service.GetTimeToFillAsync(Period with { RequisitionId = AcceptanceSeedIds.RecruitmentReportsRequisition1Id }, unrestricted);
        Assert.Equal(new DurationMeasure(37m, 37m, 1, 0), fill.Days);

        var branch = OrgAccessScope.Restricted(new HashSet<Guid> { AcceptanceSeedIds.Br1OrgUnitId });
        var branchTable = await service.GetRequisitionTableAsync(Period, branch);
        Assert.Contains(branchTable.Rows, r => r.RequisitionNumber == AcceptanceSeedIds.RecruitmentReportsRequisition1Number);
        Assert.DoesNotContain(branchTable.Rows, r => r.RequisitionNumber == AcceptanceSeedIds.RecruitmentReportsRequisition2Number);
        Assert.Null(await service.GetRequisitionDetailAsync(AcceptanceSeedIds.RecruitmentReportsRequisition2Id, branch, includeNames: false));

        var sla = await service.GetApprovalSlaAsync(Period, unrestricted);
        var overdue = Assert.Single(sla.Overdue, o => o.EntityId == AcceptanceSeedIds.RecruitmentReportsRequisition3Id);
        Assert.Equal(AcceptanceSeedIds.RecruitmentReportsRequisition3Number, overdue.EntityLabel);
        Assert.True(overdue.AgeHours > overdue.SlaHours);

        // Seeded history is never notified.
        var ids = await db.RecruitmentApplications.IgnoreQueryFilters()
            .Where(a => a.ApplicantFullName.StartsWith(AcceptanceSeedIds.RecruitmentReportsApplicantPrefix)).Select(a => a.Id).ToListAsync();
        Assert.Equal(0, await db.RecruitmentApplicationEvents.IgnoreQueryFilters()
            .CountAsync(e => ids.Contains(e.RecruitmentApplicationId) && e.NotificationsPlannedAt == null));
    }
}
