using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-031 (PLAN-31, P3, AC-HCM-31b): seating by capacity with random, unique exam numbers.</summary>
public sealed class Hcm031SeatingTests
{
    [Fact]
    public async Task Seats_120_admitted_applicants_across_two_halls_AC_HCM_31b()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, sessions) = await SeedExamReadyAsync(db, 120, 60, 60);

        var result = await CreateSeating(db).SeatAsync(competition.Id, Creator);

        Assert.True(result.Success, result.Error);
        Assert.Equal(120, result.Seated);
        var seated = await db.ExamCandidates.AsNoTracking().ToListAsync();
        Assert.Equal(120, seated.Count);
        Assert.Equal(60, seated.Count(c => c.ExamSessionId == sessions[0].Id));
        Assert.Equal(60, seated.Count(c => c.ExamSessionId == sessions[1].Id));
        Assert.All(seated.GroupBy(c => c.ExamSessionId), g => Assert.Equal(Enumerable.Range(1, 60), g.Select(c => c.SeatNumber).OrderBy(n => n)));
        Assert.Equal(120, seated.Select(c => c.RecruitmentApplicationId).Distinct().Count());
        Assert.Equal(RecruitmentCompetitionStatus.Examining, (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).Status);
        Assert.All(await db.ExamSessions.AsNoTracking().ToListAsync(), s => Assert.Equal(ExamSessionStatus.Seated, s.Status));
    }

    [Fact]
    public async Task Exam_numbers_are_random_fixed_length_unique_and_unrelated_to_application_order()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, _) = await SeedExamReadyAsync(db, 120, 120);
        Assert.True((await CreateSeating(db).SeatAsync(competition.Id, Creator)).Success);

        var seated = await db.ExamCandidates.AsNoTracking().ToListAsync();
        Assert.All(seated, c =>
        {
            Assert.Equal(ExamMarkingRules.ExamNumberLength, c.ExamNumber.Length);
            Assert.All(c.ExamNumber, ch => Assert.True(char.IsAsciiDigit(ch)));
        });
        Assert.Equal(seated.Count, seated.Select(c => c.ExamNumber).Distinct().Count());

        // Seats follow the exam numbers, not the application order.
        var bySeat = seated.OrderBy(c => c.SeatNumber).Select(c => c.ExamNumber).ToList();
        Assert.Equal(bySeat.OrderBy(n => n, StringComparer.Ordinal), bySeat);
        var applied = await db.RecruitmentApplications.AsNoTracking().OrderBy(a => a.AppliedAt).Select(a => a.Id).ToListAsync();
        var seatOrder = seated.OrderBy(c => c.SeatNumber).Select(c => c.RecruitmentApplicationId).ToList();
        Assert.NotEqual(applied, seatOrder);

        var generated = Enumerable.Range(0, 2000).Select(_ => ExamMarkingRules.NewExamNumber()).ToList();
        Assert.True(generated.Distinct().Count() > 1990);
        Assert.All(generated, n => Assert.NotEqual('0', n[0]));
    }

    [Fact]
    public async Task Capacity_shortfall_is_refused_with_the_shortfall_in_Arabic()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, _) = await SeedExamReadyAsync(db, 120, 50, 60);

        var result = await CreateSeating(db).SeatAsync(competition.Id, Creator);

        Assert.False(result.Success);
        Assert.Contains("(110)", result.Error, StringComparison.Ordinal);
        Assert.Contains("(120)", result.Error, StringComparison.Ordinal);
        Assert.Contains("بفارق 10", result.Error, StringComparison.Ordinal);
        Assert.Empty(await db.ExamCandidates.ToListAsync());
    }

    [Fact]
    public async Task Reseating_is_deterministic_and_keeps_exam_numbers_until_cards_are_issued()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, _) = await SeedExamReadyAsync(db, 30, 20, 20);
        var seating = CreateSeating(db);
        Assert.True((await seating.SeatAsync(competition.Id, Creator)).Success);
        var first = (await db.ExamCandidates.AsNoTracking().ToListAsync())
            .ToDictionary(c => c.RecruitmentApplicationId, c => (c.ExamNumber, c.ExamSessionId, c.SeatNumber));

        Assert.True((await seating.SeatAsync(competition.Id, Creator)).Success);
        var second = (await db.ExamCandidates.AsNoTracking().ToListAsync())
            .ToDictionary(c => c.RecruitmentApplicationId, c => (c.ExamNumber, c.ExamSessionId, c.SeatNumber));
        Assert.Equal(first, second);

        var (issued, error, count) = await seating.IssueAdmissionCardsAsync(competition.Id, Creator);
        Assert.True(issued, error);
        Assert.Equal(30, count);
        var again = await seating.SeatAsync(competition.Id, Creator);
        Assert.False(again.Success);
        Assert.Equal(HR.Core.Infrastructure.Services.Recruitment.Competitions.ExamSeatingService.CardsIssuedMessage, again.Error);
        Assert.Equal(30, await db.RecruitmentApplicationEvents.CountAsync(e => e.EventType == HR.Core.Domain.Entities.RecruitmentApplicationEventTypes.AdmissionCardReady));
    }

    [Fact]
    public async Task Only_applications_admitted_to_the_exam_are_seated()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, _) = await SeedExamReadyAsync(db, 10, 20);
        await SeedAdmittedApplicationsAsync(db, competition, 5, RecruitmentApplicationStatus.UnderReview);

        var result = await CreateSeating(db).SeatAsync(competition.Id, Creator);

        Assert.True(result.Success, result.Error);
        Assert.Equal(10, result.Seated);
    }

    [Fact]
    public void Seating_plan_fills_sessions_in_order_and_numbers_seats_from_one()
    {
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        var applicants = Enumerable.Range(0, 5).Select(i => (Guid.NewGuid(), $"1000000{i}")).ToList();

        var (seats, error) = ExamMarkingRules.PlanSeating([(s1, 3), (s2, 3)], applicants, "كتابي");

        Assert.Null(error);
        Assert.Equal([1, 2, 3], seats.Where(s => s.SessionId == s1).Select(s => s.SeatNumber));
        Assert.Equal([1, 2], seats.Where(s => s.SessionId == s2).Select(s => s.SeatNumber));
    }

    [Fact]
    public async Task A_session_needs_a_threshold_below_its_maximum_score()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, _) = await SeedExamReadyAsync(db, 1, 5);

        var (session, error) = await CreateSeating(db).CreateSessionAsync(competition.Id,
            new ExamSessionInput(ExamSessionType.Oral, "شفهي", DateTime.UtcNow.AddDays(3), 30, "قاعة", 5, 10m), Creator);

        Assert.Null(session);
        Assert.Contains("عتبة التصحيح الثالث", error, StringComparison.Ordinal);
    }
}
