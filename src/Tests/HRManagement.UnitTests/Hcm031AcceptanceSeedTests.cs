using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>HCM-031 (PLAN-31, P7): the AC-HCM-31 acceptance fixtures (<see cref="AcceptanceCompetitionPatch"/>), seeded once.</summary>
public sealed class Hcm031AcceptanceSeedTests
{
    [Fact]
    public async Task Acceptance_competition_patch_seeds_the_ac_hcm_31_fixtures_once()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var seed = new HRCoreDbContext(options))
        {
            await AcceptanceDataSeeder.SeedAsync(seed, NullLogger.Instance);
            await AcceptanceCandidateExperiencePatch.ApplyAsync(seed, NullLogger.Instance);
            await AcceptanceCompetitionPatch.ApplyAsync(seed, NullLogger.Instance);
        }

        await using var db = new HRCoreDbContext(options);
        // AC-HCM-31a: three approved, unpublished, unattached requisitions with exam criteria; two grades.
        Guid[] draftIds = [AcceptanceSeedIds.CompetitionDraftRequisition1Id, AcceptanceSeedIds.CompetitionDraftRequisition2Id, AcceptanceSeedIds.CompetitionDraftRequisition3Id];
        var drafts = await db.RecruitmentRequisitions.IgnoreQueryFilters().Where(r => draftIds.Contains(r.Id)).ToListAsync();
        Assert.Equal(3, drafts.Count);
        Assert.All(drafts, r =>
        {
            Assert.Equal(RequisitionStatus.Approved, r.Status);
            Assert.Null(r.PublishedAt);
            Assert.Null(r.PublicJobPostingId);
            Assert.Null(r.CompetitionLineId);
        });
        var draftGrades = await db.FundedPositions.IgnoreQueryFilters()
            .Where(p => drafts.Select(d => d.FundedPositionId).Contains(p.Id)).Select(p => p.GradeCode).Distinct().ToListAsync();
        Assert.Equal(2, draftGrades.Count);
        Assert.Equal(3, await db.RecruitmentAssessmentCriteria.IgnoreQueryFilters().CountAsync(c => draftIds.Contains(c.RequisitionId) && c.IsActive));

        // AC-HCM-31b–g: the competition at IntakeClosed with lines 3 + 2, frozen criteria, two sessions, 120 applicants.
        var competition = await db.RecruitmentCompetitions.IgnoreQueryFilters().Include(c => c.Lines).SingleAsync(c => c.Id == AcceptanceSeedIds.CompetitionId);
        Assert.Equal(RecruitmentCompetitionStatus.IntakeClosed, competition.Status);
        Assert.Equal(AcceptanceSeedIds.AgencyAId, competition.TenantId);
        Assert.Equal([3, 2], competition.Lines.OrderBy(l => l.LineNo).Select(l => l.RequiredCount));
        Assert.All(competition.Lines, l => Assert.NotNull(l.CriteriaVersionId));
        var attached = await db.RecruitmentRequisitions.IgnoreQueryFilters().Where(r => r.CompetitionLineId != null).ToListAsync();
        Assert.Equal(5, attached.Count);
        Assert.All(attached, r => Assert.Equal(RequisitionStatus.Published, r.Status));
        Assert.Equal(2, await db.ExamSessions.IgnoreQueryFilters().CountAsync(s => s.CompetitionId == competition.Id && s.Status == ExamSessionStatus.Planned));
        var applications = await db.RecruitmentApplications.IgnoreQueryFilters().Where(a => a.CompetitionId == competition.Id).ToListAsync();
        Assert.Equal(AcceptanceSeedIds.CompetitionApplicantCount, applications.Count);
        Assert.All(applications, a => Assert.Equal(RecruitmentApplicationStatus.Shortlisted, a.Status));
        Assert.Equal(applications.Count, applications.Select(a => a.NationalIdHash).Distinct().Count());
        Assert.Equal(2, await db.CitizenApplications.IgnoreQueryFilters()
            .CountAsync(c => c.PublicJobPostingId == AcceptanceSeedIds.CompetitionPostingId && c.NotificationConsentAt != null));
        Assert.Equal(0, await db.RecruitmentApplicationEvents.IgnoreQueryFilters()
            .CountAsync(e => applications.Select(a => a.Id).Contains(e.RecruitmentApplicationId)));
        // Dedicated positions only: no seeded VAC-* vacancy is taken.
        var positions = await db.FundedPositions.IgnoreQueryFilters()
            .Where(p => attached.Select(r => r.FundedPositionId).Contains(p.Id)).ToListAsync();
        Assert.All(positions, p => Assert.StartsWith("AC31-POS-", p.PositionCode, StringComparison.Ordinal));
        Assert.Equal("RIF", AcceptanceSeedCatalog.GetOrgUnit(positions[0].OrgUnitId).SiteCode); // br1-mgr scope (offer approval)

        // A second pass adds nothing.
        await using (var again = new HRCoreDbContext(options))
            await AcceptanceCompetitionPatch.ApplyAsync(again, NullLogger.Instance);
        await using var check = new HRCoreDbContext(options);
        Assert.Equal(AcceptanceSeedIds.CompetitionApplicantCount,
            await check.RecruitmentApplications.IgnoreQueryFilters().CountAsync(a => a.CompetitionId == competition.Id));
        Assert.Equal(8, await check.RecruitmentRequisitions.IgnoreQueryFilters().CountAsync(r => r.RequisitionNumber.StartsWith("REQ-2026-A-AC31")));
    }
}
