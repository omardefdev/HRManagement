using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

public class AcceptanceSeedTests
{
    [Fact]
    public async Task Acceptance_seed_is_idempotent()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db1 = new HRCoreDbContext(options);
        await AcceptanceDataSeeder.SeedAsync(db1, NullLogger.Instance);

        Assert.Equal(3, await db1.Tenants.IgnoreQueryFilters().CountAsync());
        Assert.Equal(98, await db1.OrgUnits.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1617, await db1.Employees.IgnoreQueryFilters().CountAsync());

        await using var db2 = new HRCoreDbContext(options);
        await AcceptanceDataSeeder.SeedAsync(db2, NullLogger.Instance);
        Assert.Equal(3, await db2.Tenants.IgnoreQueryFilters().CountAsync());
        Assert.Equal(98, await db2.OrgUnits.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1617, await db2.Employees.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Acceptance_seed_scores_match_assessment_formula()
    {
        // HCM-026: seeded AssessmentScore must be reproducible from the seeded structured scores.
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new HRCoreDbContext(options);
        await AcceptanceDataSeeder.SeedAsync(db, NullLogger.Instance);

        var criteria = await db.RecruitmentAssessmentCriteria.IgnoreQueryFilters().ToListAsync();
        Assert.NotEmpty(criteria);
        Assert.All(criteria, c => Assert.True(c.IsActive));
        Assert.All(criteria, c => Assert.Null(HR.Core.Domain.Services.AssessmentCriteriaRules.Validate(
            HR.Core.Domain.Recruitment.AssessmentCriteriaSerializer.Deserialize(c.CriteriaJson))));

        var scored = await db.RecruitmentApplications.IgnoreQueryFilters()
            .Where(a => a.AssessmentScore != null && a.CriteriaVersionId != null)
            .ToListAsync();
        Assert.NotEmpty(scored);
        foreach (var app in scored)
        {
            var version = criteria.Single(c => c.Id == app.CriteriaVersionId);
            var items = HR.Core.Domain.Recruitment.AssessmentCriteriaSerializer.Deserialize(version.CriteriaJson);
            var rows = await db.RecruitmentAssessmentScores.IgnoreQueryFilters()
                .Where(s => s.RecruitmentApplicationId == app.Id)
                .ToListAsync();
            var final = HR.Core.Domain.Services.AssessmentScoringRules.ComputeFinal(
                items,
                rows.GroupBy(r => r.AssessorUserId)
                    .Select(g => g.Select(r => new HR.Core.Domain.Services.AssessmentScoringRules.AssessorScore(r.ItemKey, r.Score))));
            Assert.Equal(app.AssessmentScore, final);
            Assert.NotNull(version.LockedAt);
        }
    }

    [Fact]
    public async Task Acceptance_seed_published_requisitions_have_closing_dates()
    {
        // HCM-026 (F2): published requisitions, their postings and vacancies share the seeded closing date.
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new HRCoreDbContext(options);
        await AcceptanceDataSeeder.SeedAsync(db, NullLogger.Instance);

        var published = await db.RecruitmentRequisitions.IgnoreQueryFilters()
            .Where(r => r.Status == HR.Core.Domain.Enums.RequisitionStatus.Published)
            .ToListAsync();
        Assert.NotEmpty(published);
        Assert.All(published, r => Assert.NotNull(r.ApplicationClosingDate));
        Assert.All(published, r => Assert.True(r.ApplicationClosingDate > DateOnly.FromDateTime(DateTime.UtcNow)));

        var ids = published.Select(r => r.Id).ToList();
        var postings = await db.PublicJobPostings.IgnoreQueryFilters().Where(p => p.RequisitionId != null && ids.Contains(p.RequisitionId.Value)).ToListAsync();
        var vacancies = await db.InternalVacancies.IgnoreQueryFilters().Where(v => v.RequisitionId != null && ids.Contains(v.RequisitionId.Value)).ToListAsync();
        Assert.NotEmpty(postings);
        Assert.All(postings, p => Assert.Equal(published.Single(r => r.Id == p.RequisitionId).ApplicationClosingDate, p.ClosingDate));
        Assert.All(vacancies, v => Assert.Equal(published.Single(r => r.Id == v.RequisitionId).ApplicationClosingDate, v.ClosingDate));
    }

    [Fact]
    public void AcceptanceSeedIds_are_stable()
    {
        Assert.Equal("AGENCY-A", AcceptanceSeedIds.AgencyACode);
        Assert.Equal(AcceptanceSeedIds.Br1OrgUnitId, Guid.Parse("a0000001-0000-4000-8000-000000000102"));
        Assert.Equal(AcceptanceSeedIds.EmpBId, Guid.Parse("a0000002-0000-4000-8000-000000000201"));
    }
}
