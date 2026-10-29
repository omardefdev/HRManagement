using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-031 (PLAN-31, P5, decision 7): bulk offer drafts with the salary rule, listed failures, submission into the existing approval.</summary>
public sealed class Hcm031BulkOfferTests
{
    private static async Task<MarkedCompetition> SeedAllocatedAsync(HRCoreDbContext db)
    {
        var fx = await SeedMarkedAsync(db, [90m, 85m, 80m]);
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        Assert.True((await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator)).Success);
        return fx;
    }

    private static CompetitionOfferService Create(HRCoreDbContext db, IPayrollGradeLookup lookup, CompetitionOfferSalaryRule rule) =>
        new(db, CreateOfferServiceWithRecall(db, lookup), lookup,
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions { Competition = { DefaultOfferSalary = rule } }));

    [Theory]
    [InlineData(CompetitionOfferSalaryRule.GradeMin, 500_000)]
    [InlineData(CompetitionOfferSalaryRule.GradeBase, 600_000)]
    public async Task Drafts_take_the_line_grade_and_the_configured_salary_rule(CompetitionOfferSalaryRule rule, int expected)
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedAllocatedAsync(db);
        var lookup = new FakePayrollGradeLookup { Result = new PayrollGradeLookupResult.Found(500_000m, 900_000m, 600_000m, "درجة") };

        var results = await Create(db, lookup, rule).CreateDraftsAsync(fx.Competition.Id, null, DateTime.UtcNow.Date.AddDays(20), null, Creator);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success, r.Error));
        var offers = await db.OfferLetters.AsNoTracking().Include(o => o.RecruitmentApplication).ThenInclude(a => a!.CompetitionLine).ToListAsync();
        Assert.All(offers, o =>
        {
            Assert.Equal(OfferStatus.Draft, o.Status);
            Assert.Equal(expected, o.OfferedSalary);
            Assert.Equal(o.RecruitmentApplication!.CompetitionLine!.GradeCode, o.GradeCode);
        });
        Assert.All(lookup.Calls, c => Assert.Contains(c.GradeCode, new[] { "G5", "G6" }));

        // A second run creates nothing new (every allocated candidate has an active offer).
        Assert.Empty(await Create(db, lookup, rule).CreateDraftsAsync(fx.Competition.Id, null, DateTime.UtcNow.Date.AddDays(20), null, Creator));
    }

    [Fact]
    public async Task Failures_are_listed_per_item_and_never_silent()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedAllocatedAsync(db);
        var lookup = new FakePayrollGradeLookup { Result = new PayrollGradeLookupResult.NotFound() };

        var results = await Create(db, lookup, CompetitionOfferSalaryRule.GradeMin)
            .CreateDraftsAsync(fx.Competition.Id, null, DateTime.UtcNow.Date.AddDays(20), null, Creator);

        Assert.Equal(3, results.Count);
        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.Contains("غير معرّفة في الرواتب", r.Error, StringComparison.Ordinal);
        });
        Assert.Empty(await db.OfferLetters.ToListAsync());

        var notFinalized = await Create(db, lookup, CompetitionOfferSalaryRule.GradeMin)
            .CreateDraftsAsync(Guid.NewGuid(), null, DateTime.UtcNow.Date.AddDays(20), null, Creator);
        Assert.False(Assert.Single(notFinalized).Success);
    }

    [Fact]
    public async Task Submitting_puts_every_draft_into_the_existing_offer_approval()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedAllocatedAsync(db);
        var lookup = new FakePayrollGradeLookup { Result = new PayrollGradeLookupResult.Found(500_000m, 900_000m, 600_000m, "درجة") };
        var service = Create(db, lookup, CompetitionOfferSalaryRule.GradeMin);
        await service.CreateDraftsAsync(fx.Competition.Id, null, DateTime.UtcNow.Date.AddDays(20), null, Creator);

        var results = await service.SubmitDraftsAsync(fx.Competition.Id, null, Creator, "creator");

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success, r.Error));
        foreach (var offer in await db.OfferLetters.AsNoTracking().ToListAsync())
        {
            Assert.Equal(OfferStatus.PendingApproval, offer.Status);
            Assert.Equal(RecruitmentOfferService.OfferApprovalWorkflowCode, await Hcm027TestSupport.GetWorkflowCodeAsync(db, offer.WorkflowInstanceId));
        }
    }
}
