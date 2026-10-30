using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-031 (PLAN-31, P5, AC-HCM-31f): finalization — allocation by merit and preference, line reserves, rejections.</summary>
public sealed class Hcm031AllocationTests
{
    /// <summary>
    /// Lines L1 (3 vacancies) and L2 (2). Marks 90..40: c0 [L2,L1], c1 [L1,L2], c2 [L2,L1], c3 [L2,L1] (L2 full → L1),
    /// c4 [L1], c5 [L1,L2] (both full → reserve on both), c6 [L2] (reserve on L2), c7 fails.
    /// </summary>
    private static async Task<(MarkedCompetition Fx, CompetitionLine L1, CompetitionLine L2)> SeedAc31fAsync(HRCoreDbContext db)
    {
        var fx = await SeedMarkedAsync(db, [90m, 85m, 80m, 75m, 70m, 65m, 60m, 40m]);
        var lines = fx.Competition.Lines.OrderBy(l => l.LineNo).ToList();
        var (l1, l2) = (lines[0], lines[1]);
        var a = fx.Applications;
        await SetPreferencesAsync(db, a[0], l2.Id, l1.Id);
        await SetPreferencesAsync(db, a[1], l1.Id, l2.Id);
        await SetPreferencesAsync(db, a[2], l2.Id, l1.Id);
        await SetPreferencesAsync(db, a[3], l2.Id, l1.Id);
        await SetPreferencesAsync(db, a[4], l1.Id);
        await SetPreferencesAsync(db, a[5], l1.Id, l2.Id);
        await SetPreferencesAsync(db, a[6], l2.Id);
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        return (fx, l1, l2);
    }

    [Fact]
    public async Task Finalize_ranks_five_by_merit_and_preference_and_puts_the_rest_on_line_reserves_AC_HCM_31f()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (fx, l1, l2) = await SeedAc31fAsync(db);

        var result = await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator);

        Assert.True(result.Success, result.Error);
        Assert.Equal((5, 2, 1), (result.Allocated, result.Reserve, result.NotSuccessful));
        var apps = (await db.RecruitmentApplications.AsNoTracking().ToListAsync()).ToDictionary(x => x.Id);
        var a = fx.Applications.Select(x => apps[x.Id]).ToList();

        Assert.All(new[] { a[0], a[1], a[2], a[3], a[4] }, x => Assert.Equal(RecruitmentApplicationStatus.Ranked, x.Status));
        Assert.Equal(l2.Id, a[0].CompetitionLineId);
        Assert.Equal(l1.Id, a[1].CompetitionLineId);
        Assert.Equal(l2.Id, a[2].CompetitionLineId);
        Assert.Equal(l1.Id, a[3].CompetitionLineId);
        Assert.Equal(l1.Id, a[4].CompetitionLineId);
        Assert.Equal([1, 2, 3, 4, 5], a.Take(5).Select(x => x.Rank!.Value));
        Assert.Equal(5, a.Take(5).Select(x => x.RequisitionId).Distinct().Count());

        // Within a line the vacancies are filled in requisition-number order.
        var l1Requisitions = await db.RecruitmentRequisitions.AsNoTracking().Where(r => r.CompetitionLineId == l1.Id)
            .OrderBy(r => r.RequisitionNumber).Select(r => r.Id).ToListAsync();
        Assert.Equal(l1Requisitions, new[] { a[1], a[3], a[4] }.Select(x => x.RequisitionId!.Value));

        var allocations = await db.CompetitionAllocations.AsNoTracking().ToDictionaryAsync(x => x.RecruitmentApplicationId);
        Assert.Equal(2, allocations[a[3].Id].PreferenceOrderSatisfied);
        Assert.Equal(1, allocations[a[0].Id].PreferenceOrderSatisfied);

        Assert.Equal(RecruitmentApplicationStatus.ReserveListed, a[5].Status);
        Assert.Equal(RecruitmentApplicationStatus.ReserveListed, a[6].Status);
        Assert.Null(a[5].RequisitionId);
        var reserves = await db.CompetitionReserveEntries.AsNoTracking().ToListAsync();
        Assert.Equal(1, reserves.Single(e => e.RecruitmentApplicationId == a[5].Id && e.CompetitionLineId == l1.Id).LineRank);
        Assert.Equal(1, reserves.Single(e => e.RecruitmentApplicationId == a[5].Id && e.CompetitionLineId == l2.Id).LineRank);
        Assert.Equal(2, reserves.Single(e => e.RecruitmentApplicationId == a[6].Id && e.CompetitionLineId == l2.Id).LineRank);
        var competition = await db.RecruitmentCompetitions.AsNoTracking().SingleAsync();
        Assert.All(reserves, e => Assert.Equal(DateOnly.FromDateTime(competition.FinalizedAt!.Value).AddMonths(competition.ReserveValidityMonths), e.ReserveUntil));

        Assert.Equal(RecruitmentApplicationStatus.Rejected, a[7].Status);
        Assert.Equal(RecruitmentRejectionKind.CompetitionResult, a[7].RejectionKind);
        Assert.Equal(RecruitmentCompetitionStatus.Finalized, competition.Status);
        Assert.Equal(5, await db.RecruitmentApplicationEvents.CountAsync(e => e.EventType == RecruitmentApplicationEventTypes.CompetitionAllocated));
    }

    [Fact]
    public async Task Finalize_is_idempotent()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (fx, _, _) = await SeedAc31fAsync(db);
        var allocation = CreateAllocation(db);
        Assert.True((await allocation.FinalizeAsync(fx.Competition.Id, Creator)).Success);

        var again = await allocation.FinalizeAsync(fx.Competition.Id, Creator);

        Assert.True(again.Success);
        Assert.True(again.AlreadyFinalized);
        Assert.Equal(5, await db.CompetitionAllocations.CountAsync());
        Assert.Equal(3, await db.CompetitionReserveEntries.CountAsync());
    }

    [Fact]
    public async Task Finalize_is_blocked_before_the_window_ends_and_while_an_objection_is_pending()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [70m, 40m]);
        await PublishResultsAsync(db, fx.Competition.Id);
        var allocation = CreateAllocation(db);

        Assert.Contains("مدة الاعتراض", (await allocation.FinalizeAsync(fx.Competition.Id, Creator)).Error, StringComparison.Ordinal);

        var objections = new CompetitionObjectionService(db, Hcm028AppealTestSupport.CreateService(db), CreateResults(db));
        Assert.NotNull((await objections.FileAsync(fx.Applications[1].Id, null, "أعترض على احتساب علامة الدفتر بشكل خاطئ", RecruitmentAppealFiledBy.Candidate, null)).Appeal);
        var competition = await db.RecruitmentCompetitions.SingleAsync();
        competition.ResultsFirstPublishedAt = DateTime.UtcNow.AddDays(-(competition.ObjectionWindowDays + 1));
        await db.SaveChangesAsync();

        var blocked = await allocation.FinalizeAsync(fx.Competition.Id, Creator);
        Assert.False(blocked.Success);
        Assert.Contains("اعتراض قيد المراجعة", blocked.Error, StringComparison.Ordinal);
        Assert.Equal(RecruitmentCompetitionStatus.ResultsPublished, (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.CompetitionAllocations.ToListAsync());
    }

    [Fact]
    public async Task Capacity_exhaustion_and_line_pass_marks_decide_reserve_or_rejection()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [90m, 80m, 70m, 55m]);
        var lines = fx.Competition.Lines.OrderBy(l => l.LineNo).ToList();
        var l2 = await db.CompetitionLines.SingleAsync(l => l.Id == lines[1].Id);
        l2.LinePassMark = 60m;
        await db.SaveChangesAsync();
        foreach (var app in fx.Applications)
            await SetPreferencesAsync(db, app, l2.Id);
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);

        var result = await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator);

        Assert.True(result.Success, result.Error);
        var apps = (await db.RecruitmentApplications.AsNoTracking().ToListAsync()).ToDictionary(x => x.Id);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, apps[fx.Applications[0].Id].Status);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, apps[fx.Applications[1].Id].Status);
        Assert.Equal(RecruitmentApplicationStatus.ReserveListed, apps[fx.Applications[2].Id].Status); // L2 full
        // 55 is below the own pass mark (60) of its only preferred line: failed, no line, no reserve.
        Assert.Equal(RecruitmentApplicationStatus.Rejected, apps[fx.Applications[3].Id].Status);
        Assert.Equal(RecruitmentRejectionKind.CompetitionResult, apps[fx.Applications[3].Id].RejectionKind);
    }

    [Fact]
    public void Equal_ranks_fall_back_to_the_exam_number_and_unknown_lines_are_ignored()
    {
        var line = new AllocationLine(Guid.NewGuid(), [Guid.NewGuid()], 50m);
        var first = new AllocationCandidate(Guid.NewGuid(), 1, "20000000", 80m, [Guid.NewGuid(), line.LineId]);
        var second = new AllocationCandidate(Guid.NewGuid(), 1, "10000000", 80m, [line.LineId]);

        var plan = CompetitionAllocationRules.Plan([first, second], [line]);

        Assert.Equal(second.ApplicationId, Assert.Single(plan.Placements).ApplicationId);
        Assert.Equal(first.ApplicationId, Assert.Single(plan.Reserves).ApplicationId);
    }

    [Fact]
    public async Task Applications_outside_the_competition_are_never_touched()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (fx, _, _) = await SeedAc31fAsync(db);
        var requisition = await SeedApprovedRequisitionAsync(db, "G5", status: RequisitionStatus.Published);
        var outside = new RecruitmentApplication
        {
            ApplicantFullName = "خارج المسابقة",
            RequisitionId = requisition.Id,
            AppliedPosition = requisition.JobTitle,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Ranked,
            Rank = 1
        };
        db.RecruitmentApplications.Add(outside);
        await db.SaveChangesAsync();

        Assert.True((await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator)).Success);

        var saved = await db.RecruitmentApplications.AsNoTracking().SingleAsync(x => x.Id == outside.Id);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, saved.Status);
        Assert.Equal(requisition.Id, saved.RequisitionId);
        Assert.Equal(1, saved.Rank);
    }
}
