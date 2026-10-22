using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
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
    public async Task Acceptance_seed_committee_gated_requisition_is_ready_for_votes()
    {
        // HCM-028: gated requisition + 3 assessed candidates + unsubmitted session (chair + 3, not persona-linked).
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new HRCoreDbContext(options);
        await AcceptanceDataSeeder.SeedAsync(db, NullLogger.Instance);

        var requisition = await db.RecruitmentRequisitions.IgnoreQueryFilters()
            .SingleAsync(r => r.Id == AcceptanceSeedIds.CommitteeRequisitionId);
        Assert.True(requisition.RequiresCommitteeApproval);
        Assert.Equal(HR.Core.Domain.Enums.RequisitionStatus.Published, requisition.Status);
        Assert.Equal(AcceptanceSeedIds.AgencyAId, requisition.TenantId);
        Assert.Equal(1, await db.RecruitmentRequisitions.IgnoreQueryFilters()
            .CountAsync(r => r.FundedPositionId == requisition.FundedPositionId));
        // br1-mgr (RIF scope) must be able to act on committee steps 1-2.
        var position = await db.FundedPositions.IgnoreQueryFilters().SingleAsync(p => p.Id == requisition.FundedPositionId);
        Assert.Equal("RIF", AcceptanceSeedCatalog.GetOrgUnit(position.OrgUnitId).SiteCode);
        Assert.Equal(AcceptancePersonaCatalog.Get("br1-mgr").ScopeOrgUnitId, AcceptanceSeedIds.Br1OrgUnitId);

        var candidates = await db.RecruitmentApplications.IgnoreQueryFilters()
            .Where(a => a.RequisitionId == requisition.Id)
            .ToListAsync();
        Assert.Equal(3, candidates.Count);
        Assert.All(candidates, a => Assert.Equal(HR.Core.Domain.Enums.RecruitmentApplicationStatus.Assessed, a.Status));
        Assert.Equal(3, candidates.Select(a => a.AssessmentScore).Distinct().Count());

        var session = await db.SelectionCommitteeSessions.IgnoreQueryFilters()
            .Include(s => s.Members)
            .SingleAsync(s => s.Id == AcceptanceSeedIds.CommitteeSessionId);
        Assert.Null(session.SubmittedAt);
        Assert.Null(session.LockedAt);
        Assert.Null(session.WorkflowInstanceId);
        Assert.Equal(HR.Core.Domain.Enums.CommitteePurpose.Ranking, session.Purpose);
        Assert.Equal(4, session.Members.Count);
        Assert.Single(session.Members, m => HR.Core.Domain.Services.SelectionCommitteeRules.IsChairRole(m.Role));
        Assert.Equal(session.Members.Single(m => m.Role == "Chair").EmployeeId, session.ChairEmployeeId);
        Assert.All(session.Members, m => Assert.False(m.Recused));

        var personaEmployees = AcceptancePersonaCatalog.Personas
            .Where(p => p.Employee is not null)
            .ToDictionary(p => p.Employee!.Id, p => p.Id);
        foreach (var personaId in new[] { "a-management-approver", "br1-mgr", "central-a" })
            Assert.DoesNotContain(session.Members, m => personaEmployees.TryGetValue(m.EmployeeId, out var p) && p == personaId);
        Assert.All(session.Members, m => Assert.False(personaEmployees.ContainsKey(m.EmployeeId)));

        // HCM-028 session 2: adding a-compliance-2 must not shift the documented members (handoff §6).
        Assert.Equal(
            new[]
            {
                Guid.Parse("e4b65b57-584f-d842-8c6b-4324441cad16"), Guid.Parse("4c2fe080-6243-d24b-93a4-4cce36fbbd68"),
                Guid.Parse("38fe721e-a767-3e4a-a448-cae5ef8c3455"), Guid.Parse("ec716ac1-3e93-4245-9c65-f0c67c8c1f4c")
            }.Order(),
            session.Members.Select(m => m.EmployeeId).Order());
        var compliance2 = AcceptancePersonaCatalog.Get("a-compliance-2").Employee!;
        Assert.Equal(AcceptanceEmployeeState.Active, compliance2.State);
        Assert.NotEqual(AcceptancePersonaCatalog.Get("a-compliance").Employee!.Id, compliance2.Id);

        // Quorum and chair are met; only the per-candidate decisions (and their votes) remain.
        var members = session.Members.ToList();
        var issues = HR.Core.Domain.Services.SelectionCommitteeRules.GetSubmitIssues(members, [], [], [], candidates);
        Assert.Equal(3, issues.Count);
        Assert.All(issues, i => Assert.Contains("لا توجد توصية", i, StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptanceSeedIds_are_stable()
    {
        Assert.Equal("AGENCY-A", AcceptanceSeedIds.AgencyACode);
        Assert.Equal(AcceptanceSeedIds.Br1OrgUnitId, Guid.Parse("a0000001-0000-4000-8000-000000000102"));
        Assert.Equal(AcceptanceSeedIds.EmpBId, Guid.Parse("a0000002-0000-4000-8000-000000000201"));
    }
}
