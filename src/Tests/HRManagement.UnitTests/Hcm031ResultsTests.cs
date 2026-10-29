using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-031 (PLAN-31, P4, AC-HCM-31d): results — weights and normalization, the pass mark, ranks and ties, the version
/// hash, approval with separation of duties, publication without names, immutability, and the committee-gate guards.
/// </summary>
public sealed class Hcm031ResultsTests
{
    [Fact]
    public void Exam_points_are_the_mark_normalized_to_the_exam_weight_plus_the_other_stages()
    {
        Assert.Equal(56m, CompetitionScoringRules.ExamPoints(70m, [(80m, 100m)]));
        Assert.Equal(52.5m, CompetitionScoringRules.ExamPoints(70m, [(80m, 100m), (14m, 20m)])); // (0.8 + 0.7) / 2 × 70
        var scored = CompetitionScoringRules.Compute(
            [Input(Guid.NewGuid(), "10000001", [(80m, 100m)], nonExam: 24m)],
            70m, [CompetitionTieBreak.ExamScore]);
        Assert.Equal(80m, scored[0].FinalScore);
        Assert.Equal(56m, scored[0].ExamScore);
    }

    [Fact]
    public void The_pass_mark_decides_the_outcome_and_absentees_get_no_rank()
    {
        var pass = Input(Guid.NewGuid(), "10000001", [(50m, 100m)]);
        var fail = Input(Guid.NewGuid(), "10000002", [(49.99m, 100m)]);
        var absent = Input(Guid.NewGuid(), "10000003", [], absent: true);
        var scored = CompetitionScoringRules.Compute([pass, fail, absent], 100m, [CompetitionTieBreak.ExamScore]).ToDictionary(s => s.ExamNumber);

        Assert.Equal(CompetitionResultOutcome.Passed, scored["10000001"].Outcome);
        Assert.Equal(CompetitionResultOutcome.Failed, scored["10000002"].Outcome);
        Assert.Equal(CompetitionResultOutcome.Absent, scored["10000003"].Outcome);
        Assert.Equal(1, scored["10000001"].Rank);
        Assert.Equal(2, scored["10000002"].Rank);
        Assert.Null(scored["10000003"].Rank);
        Assert.Null(scored["10000003"].FinalScore);
    }

    [Fact]
    public void The_tie_break_order_is_applied_and_a_remaining_tie_shares_the_rank_and_is_flagged()
    {
        var at = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var a = Input(Guid.NewGuid(), "10000001", [(70m, 100m)], appliedAt: at, interview: 5m);
        var b = Input(Guid.NewGuid(), "10000002", [(70m, 100m)], appliedAt: at, interview: 9m);
        var c = Input(Guid.NewGuid(), "10000003", [(70m, 100m)], appliedAt: at, interview: 9m);
        var d = Input(Guid.NewGuid(), "10000004", [(60m, 100m)], appliedAt: at);

        var scored = CompetitionScoringRules.Compute([a, b, c, d], 100m, [CompetitionTieBreak.InterviewScore, CompetitionTieBreak.AppliedAt])
            .ToDictionary(s => s.ExamNumber);

        Assert.Equal(1, scored["10000002"].Rank);
        Assert.Equal(1, scored["10000003"].Rank);
        Assert.True(scored["10000002"].TieUnresolved);
        Assert.True(scored["10000003"].TieUnresolved);
        Assert.Equal(3, scored["10000001"].Rank);
        Assert.False(scored["10000001"].TieUnresolved);
        Assert.Equal(4, scored["10000004"].Rank);
    }

    [Fact]
    public async Task Computing_the_draft_uses_the_line_criteria_weights_and_the_non_exam_assessments()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [80m, 40m], ExamCriteria());
        var competition = fx.Competition;
        var criteriaId = competition.Lines.OrderBy(l => l.LineNo).First().CriteriaVersionId!.Value;
        var results = CreateResults(db);

        var (none, incomplete) = await results.ComputeDraftAsync(competition.Id, null, Creator);
        Assert.Null(none);
        Assert.Contains("لم يكتمل تقييم المراحل غير الامتحانية", incomplete, StringComparison.Ordinal);

        foreach (var (application, score) in fx.Applications.Zip(new[] { 8m, 10m }))
            db.RecruitmentAssessmentScores.Add(new RecruitmentAssessmentScore
            {
                RecruitmentApplicationId = application.Id,
                CriteriaVersionId = criteriaId,
                ItemKey = "INT",
                AssessorUserId = Guid.NewGuid(),
                Score = score,
                NoConflictDeclared = true,
                SubmittedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var (version, error) = await results.ComputeDraftAsync(competition.Id, null, Creator);

        Assert.True(version is not null, error);
        var rows = await db.CompetitionResults.AsNoTracking().Where(r => r.ResultVersionId == version!.Id).ToDictionaryAsync(r => r.RecruitmentApplicationId);
        Assert.Equal(80m, rows[fx.Applications[0].Id].FinalScore); // 0.8 × 70 + 8/10 × 30
        Assert.Equal(58m, rows[fx.Applications[1].Id].FinalScore); // 0.4 × 70 + 10/10 × 30
        Assert.Equal(CompetitionResultOutcome.Passed, rows[fx.Applications[1].Id].Outcome);
        Assert.Equal(1, rows[fx.Applications[0].Id].OverallRank);
        Assert.Equal(RecruitmentCompetitionStatus.ResultsDraft, (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Results_are_refused_until_marking_is_closed()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, _) = await SeedExamReadyWithCriteriaAsync(db, ExamOnlyCriteria(), 2, 2);
        Assert.True((await CreateSeating(db).SeatAsync(competition.Id, Creator)).Success);

        var (version, error) = await CreateResults(db).ComputeDraftAsync(competition.Id, null, Creator);

        Assert.Null(version);
        Assert.Contains("إغلاق التصحيح", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unresolved_tie_blocks_the_approval_until_HR_records_a_decision()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [70m, 70m, 90m]);
        var competition = await db.RecruitmentCompetitions.SingleAsync();
        competition.TieBreakOrder = nameof(CompetitionTieBreak.ExamScore);
        await db.SaveChangesAsync();
        var results = CreateResults(db);
        var (version, _) = await results.ComputeDraftAsync(competition.Id, null, Creator);
        var tied = await db.CompetitionResults.Where(r => r.ResultVersionId == version!.Id && r.TieUnresolved).ToListAsync();
        Assert.Equal(2, tied.Count);
        Assert.All(tied, r => Assert.Equal(2, r.OverallRank));

        var (submitted, error) = await results.SubmitForApprovalAsync(version!.Id, Creator, "creator");
        Assert.False(submitted);
        Assert.Contains(CompetitionScoringRules.UnresolvedTieMessage, error, StringComparison.Ordinal);

        var shaBefore = (await db.CompetitionResultVersions.AsNoTracking().SingleAsync()).Sha256;
        var order = new[] { fx.Applications[1].Id, fx.Applications[0].Id };
        Assert.False((await results.ResolveTieAsync(version.Id, order, "قصير", Creator)).Success);
        Assert.True((await results.ResolveTieAsync(version.Id, order, "قرار اللجنة بتقديم صاحب الخبرة الأطول", Creator)).Success);

        var ranks = await db.CompetitionResults.AsNoTracking().Where(r => r.ResultVersionId == version.Id).ToDictionaryAsync(r => r.RecruitmentApplicationId);
        Assert.Equal(2, ranks[fx.Applications[1].Id].OverallRank);
        Assert.Equal(3, ranks[fx.Applications[0].Id].OverallRank);
        Assert.NotEqual(shaBefore, (await db.CompetitionResultVersions.AsNoTracking().SingleAsync()).Sha256);
        Assert.True((await results.SubmitForApprovalAsync(version.Id, Creator, "creator")).Success);
    }

    [Fact]
    public async Task The_version_hash_is_of_the_canonical_results_and_stable()
    {
        using var db = Hcm027TestSupport.CreateContext();
        await SeedMarkedAsync(db, [70m, 55m, null]);
        var results = CreateResults(db);
        var (first, _) = await results.ComputeDraftAsync((await db.RecruitmentCompetitions.SingleAsync()).Id, null, Creator);
        var sha = first!.Sha256;
        var rows = await db.CompetitionResults.AsNoTracking().Where(r => r.ResultVersionId == first.Id).ToListAsync();

        Assert.Equal(sha, CompetitionScoringRules.ComputeSha256(first.VersionNo, rows));
        Assert.Equal(sha, CompetitionScoringRules.ComputeSha256(first.VersionNo, rows.AsEnumerable().Reverse()));
        var (again, _) = await results.ComputeDraftAsync((await db.RecruitmentCompetitions.SingleAsync()).Id, null, Creator);
        Assert.Equal(first.Id, again!.Id); // the open draft is recomputed in place
        Assert.Equal(sha, again.Sha256);
    }

    [Fact]
    public async Task Approval_needs_two_distinct_people_other_than_the_computer_then_publication_supersedes_AC_HCM_31d()
    {
        using var db = Hcm027TestSupport.CreateContext();
        await SeedMarkedAsync(db, [70m, 40m]);
        var competition = await db.RecruitmentCompetitions.SingleAsync();
        var results = CreateResults(db);
        var (version, _) = await results.ComputeDraftAsync(competition.Id, null, Creator);
        Assert.True((await results.SubmitForApprovalAsync(version!.Id, Creator, "creator")).Success);
        Assert.Equal(RecruitmentCompetitionStatus.ResultsPendingApproval, (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).Status);
        var instanceId = (await db.CompetitionResultVersions.AsNoTracking().SingleAsync()).WorkflowInstanceId!.Value;
        Assert.Equal(CompetitionResultsService.ApprovalWorkflowCode, await Hcm027TestSupport.GetWorkflowCodeAsync(db, instanceId));
        var actions = CreateActionService(db);

        Assert.False((await actions.ApproveAsync(instanceId, Actor(Creator), null, null)).Success);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Reviewer), null, null)).Success);
        var same = await actions.ApproveAsync(instanceId, Actor(Reviewer, "hr-management-approver"), null, null);
        Assert.False(same.Success);
        Assert.Equal("يجب أن يعتمد كل خطوة من خطوات اعتماد النتائج شخص مختلف.", same.Error);
        Assert.False((await results.PublishAsync(version.Id, Creator)).Success);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Approver, "hr-management-approver"), null, null)).Success);

        Assert.True((await results.PublishAsync(version.Id, Creator)).Success);

        var saved = await db.RecruitmentCompetitions.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentCompetitionStatus.ResultsPublished, saved.Status);
        Assert.Equal(version.Id, saved.CurrentResultVersionId);
        Assert.NotNull(saved.ResultsFirstPublishedAt);
        Assert.Equal(2, await db.RecruitmentApplicationEvents.CountAsync(e => e.EventType == RecruitmentApplicationEventTypes.ResultsPublished));
    }

    [Fact]
    public async Task A_rejected_approval_returns_the_results_to_draft()
    {
        using var db = Hcm027TestSupport.CreateContext();
        await SeedMarkedAsync(db, [70m]);
        var competition = await db.RecruitmentCompetitions.SingleAsync();
        var results = CreateResults(db);
        var (version, _) = await results.ComputeDraftAsync(competition.Id, null, Creator);
        Assert.True((await results.SubmitForApprovalAsync(version!.Id, Creator, "creator")).Success);
        var instanceId = (await db.CompetitionResultVersions.AsNoTracking().SingleAsync()).WorkflowInstanceId!.Value;

        Assert.True((await CreateActionService(db).RejectAsync(instanceId, Actor(Reviewer), "مراجعة احتساب الدفتر الأول")).Success);

        var saved = await db.CompetitionResultVersions.AsNoTracking().SingleAsync();
        Assert.Equal(CompetitionResultVersionStatus.Draft, saved.Status);
        Assert.Equal("مراجعة احتساب الدفتر الأول", saved.ReturnReason);
        Assert.Equal(RecruitmentCompetitionStatus.ResultsDraft, (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task The_public_list_shows_exam_numbers_and_outcomes_only_AC_HCM_31d()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [70m, 40m, null]);
        await PublishResultsAsync(db, fx.Competition.Id);

        var view = await CreateResults(db).GetPublicAsync(fx.Competition.Id, 1);

        Assert.NotNull(view);
        Assert.Equal(3, view.Total);
        Assert.Equal(["ExamNumber", "Outcome"], typeof(PublicResultRow).GetProperties().Select(p => p.Name).OrderBy(n => n));
        var json = JsonSerializer.Serialize(view.Rows);
        foreach (var application in fx.Applications)
        {
            Assert.DoesNotContain(application.ApplicantFullName, json, StringComparison.Ordinal);
            Assert.DoesNotContain(application.Id.ToString(), json, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains(view.Rows, r => r.Outcome == CompetitionResultOutcome.Absent);
        Assert.Equal(view.Competition.ResultsFirstPublishedAt!.Value.AddDays(view.Competition.ObjectionWindowDays), view.ObjectionDeadlineUtc);

        var pdf = HR.Core.Web.Services.CompetitionResultsDocument.Generate(view.Competition, view.Version, view.Rows, view.ObjectionDeadlineUtc);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Published_results_are_never_changed_by_a_later_computation()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [70m, 40m]);
        var published = await PublishResultsAsync(db, fx.Competition.Id);
        var before = await db.CompetitionResults.AsNoTracking().Where(r => r.ResultVersionId == published.Id)
            .Select(r => new { r.ExamNumber, r.FinalScore, r.OverallRank }).ToListAsync();

        var (draft, error) = await CreateResults(db).ComputeDraftAsync(fx.Competition.Id, "إعادة احتساب", Creator);

        Assert.True(draft is not null, error);
        Assert.NotEqual(published.Id, draft!.Id);
        Assert.Equal(2, draft.VersionNo);
        var after = await db.CompetitionResults.AsNoTracking().Where(r => r.ResultVersionId == published.Id)
            .Select(r => new { r.ExamNumber, r.FinalScore, r.OverallRank }).ToListAsync();
        Assert.Equal(before, after);
        Assert.Equal(CompetitionResultVersionStatus.Published, (await db.CompetitionResultVersions.AsNoTracking().SingleAsync(v => v.Id == published.Id)).Status);
    }

    [Fact]
    public async Task Competition_ranks_never_come_from_manual_ranking_or_a_selection_committee_J4()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1);
        var application = (await SeedAdmittedApplicationsAsync(db, competition, 1))[0];
        var requisition = await db.RecruitmentRequisitions.FirstAsync(r => r.CompetitionLineId != null);
        var selection = new RecruitmentSelectionService(db);

        Assert.Equal(RecruitmentSelectionService.CompetitionRankingMessage, (await selection.AssignRankAsync(application.Id, 1)).Error);
        Assert.Equal(RecruitmentSelectionService.CompetitionRankingMessage, (await selection.ApplyCommitteeRankingAsync(requisition.Id, Creator)).Error);
        Assert.Equal(RecruitmentSelectionService.CompetitionRankingMessage,
            (await new SelectionCommitteeService(db).CreateSessionAsync(requisition.Id, DateTime.UtcNow, null, null)).Error);
    }

    private static CompetitionScoreInput Input(
        Guid id, string examNumber, IReadOnlyList<(decimal, decimal)> marks, bool absent = false, decimal nonExam = 0m,
        decimal? interview = null, DateTime? appliedAt = null) =>
        new(id, examNumber, absent, marks, nonExam, interview, null, appliedAt ?? DateTime.UtcNow, 50m);
}
