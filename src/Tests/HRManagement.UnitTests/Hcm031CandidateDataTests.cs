using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Web.Controllers;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-031 (PLAN-31, P6): the candidate's competition data outside the portal — retention clears the competition residue
/// once the competition has ended, the DSAR export carries the papers, results and allocation, and an allocated candidate's
/// hire publishes <see cref="ICandidateAccepted"/> v4 against the allocated requisition.
/// </summary>
public sealed class Hcm031CandidateDataTests
{
    private static async Task<MarkedCompetition> SeedFinalizedAsync(HRCoreDbContext db, params decimal?[] marks)
    {
        var fx = await SeedMarkedAsync(db, marks);
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        var finalized = await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator);
        Assert.True(finalized.Success, finalized.Error);
        return fx;
    }

    [Fact]
    public async Task Retention_clears_the_competition_residue_once_the_competition_has_ended()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedFinalizedAsync(db, 90m, 40m);
        var failed = fx.Applications[1];
        var paper = await db.ExamCandidates.SingleAsync(c => c.RecruitmentApplicationId == failed.Id);
        paper.AdmissionCardSha256 = new string('a', 64);
        paper.AdmissionCardIssuedAt = DateTime.UtcNow.AddYears(-6);
        foreach (var preference in await db.CompetitionPreferences.Where(p => p.RecruitmentApplicationId == failed.Id).ToListAsync())
            preference.EligibilityFlags = "[\"AGE_ABOVE_MAX\"]";
        (await db.ExamScores.FirstAsync(s => s.ExamCandidateId == paper.Id)).Note = "ملاحظة المصحح";
        await db.SaveChangesAsync();
        var retention = Hcm030RetentionJobTests.Service(db, apply: true);
        var now = DateTime.UtcNow;

        // A competition finalized today is not yet due: its applications are kept for the retention period.
        Assert.Equal(0, (await retention.CountAsync(Guid.Empty, now)).Due);

        var competition = await db.RecruitmentCompetitions.SingleAsync(c => c.Id == fx.Competition.Id);
        competition.FinalizedAt = now.AddYears(-5);
        await db.SaveChangesAsync();
        Assert.True((await retention.CountAsync(Guid.Empty, now)).Due >= 1);

        var run = Assert.Single(await retention.RunAsync(new CandidateRetentionRunRequest(now, true, CandidateRetentionRun.JobTrigger, Guid.Empty)));
        Assert.Null(run.Error);
        db.ChangeTracker.Clear();

        Assert.NotNull((await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == failed.Id)).AnonymizedAt);
        var cleared = await db.ExamCandidates.AsNoTracking().SingleAsync(c => c.Id == paper.Id);
        Assert.Null(cleared.AdmissionCardSha256);
        Assert.Null(cleared.AdmissionCardIssuedAt);
        Assert.All(await db.CompetitionPreferences.AsNoTracking().Where(p => p.RecruitmentApplicationId == failed.Id).ToListAsync(),
            p => Assert.Null(p.EligibilityFlags));
        Assert.All(await db.ExamScores.AsNoTracking().Where(s => s.ExamCandidateId == paper.Id).ToListAsync(), s => Assert.Null(s.Note));
        // The published record (exam number, marks, results) stays: it is keyed by the exam number, not by the person.
        Assert.Equal(fx.Papers[failed.Id].ExamNumber, cleared.ExamNumber);
        Assert.Equal(40m, cleared.FinalMark);
        Assert.NotEmpty(await db.CompetitionResults.AsNoTracking().Where(r => r.RecruitmentApplicationId == failed.Id).ToListAsync());
        // Nothing is left for a second run.
        Assert.Equal(0, (await retention.CountAsync(Guid.Empty, now)).Due);
    }

    [Fact]
    public async Task The_DSAR_export_carries_the_papers_the_results_and_the_allocation()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedFinalizedAsync(db, 90m, 40m);
        var winner = fx.Applications[0];

        var competition = await CompetitionRecordAsync(db, winner.Id);

        var paper = Assert.Single(competition.GetProperty("papers").EnumerateArray().ToList());
        Assert.Equal(fx.Papers[winner.Id].ExamNumber, paper.GetProperty("examNumber").GetString());
        Assert.Equal(nameof(ExamSessionType.Written), paper.GetProperty("examType").GetString());
        Assert.Equal(nameof(ExamAttendance.Present), paper.GetProperty("attendance").GetString());
        Assert.Equal(90m, paper.GetProperty("finalMark").GetDecimal());
        Assert.Equal(2, paper.GetProperty("marks").GetArrayLength());
        var result = Assert.Single(competition.GetProperty("results").EnumerateArray().ToList());
        Assert.Equal(1, result.GetProperty("version").GetInt32());
        Assert.Equal(1, result.GetProperty("rank").GetInt32());
        var allocation = Assert.Single(competition.GetProperty("allocation").EnumerateArray().ToList());
        Assert.Contains(fx.Competition.Lines.Select(l => l.Title), t => t == allocation.GetProperty("line").GetString());
        Assert.False(allocation.GetProperty("fromReserve").GetBoolean());
        Assert.Equal(0, competition.GetProperty("reserve").GetArrayLength());

        var loser = await CompetitionRecordAsync(db, fx.Applications[1].Id);
        Assert.Equal(0, loser.GetProperty("allocation").GetArrayLength());
        Assert.Equal(40m, Assert.Single(loser.GetProperty("papers").EnumerateArray().ToList()).GetProperty("finalMark").GetDecimal());
    }

    [Fact]
    public async Task An_allocated_candidate_hire_publishes_v4_against_the_allocated_requisition()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedFinalizedAsync(db, 90m);
        await ServiceRegimeSeeder.SeedAsync(db, new LegislationRegisterProvider(db));
        var allocation = await db.CompetitionAllocations.AsNoTracking().SingleAsync();
        var requisition = await db.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == allocation.AllocatedRequisitionId);
        var line = await db.CompetitionLines.AsNoTracking().SingleAsync(l => l.Id == allocation.AllocatedLineId);

        var lookup = new FakePayrollGradeLookup { Result = new PayrollGradeLookupResult.Found(500_000m, 900_000m, 600_000m, "درجة") };
        var offers = CreateOfferServiceWithRecall(db, lookup);
        var bulk = new CompetitionOfferService(db, offers, lookup, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));
        var draft = Assert.Single(await bulk.CreateDraftsAsync(fx.Competition.Id, null, DateTime.UtcNow.Date.AddDays(20), null, Creator));
        Assert.True(draft.Success, draft.Error);
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == draft.OfferId);
        offer.Status = OfferStatus.Issued;
        offer.IssuedDate = DateTime.UtcNow;
        offer.ExpiryDate = DateTime.UtcNow.AddDays(14);
        await db.SaveChangesAsync();
        var (accepted, acceptError) = await offers.AcceptAsync(offer.Id, OfferAcceptedBy.Candidate, null, null, null);
        Assert.True(accepted, acceptError);

        var winner = await db.RecruitmentApplications.SingleAsync(a => a.Id == fx.Applications[0].Id);
        Assert.Equal(requisition.Id, winner.RequisitionId);
        var publish = Hcm029TestSupport.CreatePublishMock();
        var employee = await Hcm029TestSupport.CreateClosure(db, publish.Object).HireAndCreateEmployeeAsync(
            winner, winner.NationalId!, winner.ApplicantFullName, null, "خارجي", DateTime.UtcNow.AddYears(-30), Gender.Male, null,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "PERMANENT");

        var accepted4 = Assert.Single(Hcm029TestSupport.Published(publish), p => p.Type == typeof(ICandidateAccepted)).Values;
        Assert.Equal(employee.Id, Hcm029TestSupport.Prop(accepted4, nameof(ICandidateAccepted.EmployeeId)));
        Assert.Equal(winner.Id, Hcm029TestSupport.Prop(accepted4, nameof(ICandidateAccepted.RecruitmentApplicationId)));
        Assert.Equal(offer.Id, Hcm029TestSupport.Prop(accepted4, nameof(ICandidateAccepted.OfferLetterId)));
        Assert.Equal(requisition.FundedPositionId, Hcm029TestSupport.Prop(accepted4, nameof(ICandidateAccepted.FundedPositionId)));
        Assert.Equal(line.GradeCode, Hcm029TestSupport.Prop(accepted4, nameof(ICandidateAccepted.GradeCode)));
        Assert.Equal(false, Hcm029TestSupport.Prop(accepted4, nameof(ICandidateAccepted.IsInternalMobility)));
        Assert.Equal(RecruitmentApplicationStatus.Hired,
            (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == winner.Id)).Status);
    }

    private static async Task<JsonElement> CompetitionRecordAsync(HRCoreDbContext db, Guid applicationId)
    {
        var result = await new DsarInternalController(db).GetCandidateByApplication(applicationId, default);
        var json = JsonSerializer.Serialize(((OkObjectResult)result.Result!).Value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("competition").Clone();
    }
}
