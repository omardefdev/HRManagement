using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Seeding;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-031 (PLAN-31, P7): the Acceptance v2 seed pack plus the PLAN-30 and PLAN-31 patches on a migrated PostgreSQL, as
/// hr-core-web runs them at startup — twice (idempotent). The in-memory unit test cannot see relational problems such as an
/// insert cycle within one save (competition line → criteria → requisition → line).
/// </summary>
[Trait("Category", "Testcontainers")]
public class Hcm031AcceptanceSeedIntegrationTests(Hcm026PostgresFixture fixture) : IClassFixture<Hcm026PostgresFixture>
{
    [SkippableFact]
    public async Task The_acceptance_pack_and_the_competition_patch_seed_on_PostgreSQL_twice()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);

        for (var pass = 0; pass < 2; pass++)
        {
            await using var db = fixture.CreateContext(AcceptanceSeedIds.AgencyAId);
            await AcceptanceDataSeeder.SeedAsync(db, NullLogger.Instance);
            await AcceptanceCandidateExperiencePatch.ApplyAsync(db, NullLogger.Instance);
            await AcceptanceCompetitionPatch.ApplyAsync(db, NullLogger.Instance);
        }

        await using var check = fixture.CreateContext(AcceptanceSeedIds.AgencyAId);
        var competition = await check.RecruitmentCompetitions.AsNoTracking().Include(c => c.Lines)
            .SingleAsync(c => c.Id == AcceptanceSeedIds.CompetitionId);
        Assert.Equal(RecruitmentCompetitionStatus.IntakeClosed, competition.Status);
        Assert.All(competition.Lines, l => Assert.NotNull(l.CriteriaVersionId));
        Assert.Equal(AcceptanceSeedIds.CompetitionApplicantCount,
            await check.RecruitmentApplications.AsNoTracking().CountAsync(a => a.CompetitionId == competition.Id));
        Assert.Equal(5, await check.RecruitmentRequisitions.AsNoTracking().CountAsync(r => r.CompetitionLineId != null));
        Assert.Equal(3, await check.RecruitmentRequisitions.AsNoTracking()
            .CountAsync(r => r.Status == RequisitionStatus.Approved && r.RequisitionNumber.StartsWith("REQ-2026-A-AC31D")));
    }
}
