using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-031 (PLAN-31, P4, AC-HCM-31e, decision 5): result objections over the PLAN-28 appeal — the window from the first
/// publication, one pending objection, upheld → corrected mark → new versioned results with a diff log, dismissed → no change.
/// </summary>
public sealed class Hcm031ObjectionTests
{
    private const string Reason = "أعترض على احتساب علامة الدفتر لوجود سؤال غير مصحح";
    private const string Rationale = "تبين للمراجع أن السؤال الخامس لم يُصحح وأُضيفت علامته وفق نموذج الإجابة";

    private static CompetitionObjectionService CreateObjections(HRCoreDbContext db) =>
        new(db, Hcm028AppealTestSupport.CreateService(db), CreateResults(db));

    private static async Task<(MarkedCompetition Fx, RecruitmentAppeal Appeal, Employee Reviewer)> FileAndAssignAsync(HRCoreDbContext db, decimal?[] marks, int objector)
    {
        var fx = await SeedMarkedAsync(db, marks);
        await PublishResultsAsync(db, fx.Competition.Id);
        var objections = CreateObjections(db);
        var (appeal, error) = await objections.FileAsync(fx.Applications[objector].Id, null, Reason, RecruitmentAppealFiledBy.Candidate, null);
        Assert.True(appeal is not null, error);
        var reviewer = await SeedEmployeeAsync(db, "مراجع مستقل");
        var (assigned, assignError) = await Hcm028AppealTestSupport.CreateService(db).AssignReviewerAsync(appeal!.Id, reviewer.Id, Creator, null, "hr");
        Assert.True(assigned, assignError);
        return (fx, appeal, reviewer);
    }

    [Fact]
    public async Task An_objection_is_filed_within_the_window_once_per_application()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [60m, 45m]);
        var objections = CreateObjections(db);

        Assert.Equal(CompetitionObjectionService.NoPublishedResultMessage,
            (await objections.FileAsync(fx.Applications[1].Id, null, Reason, RecruitmentAppealFiledBy.Candidate, null)).Error);

        await PublishResultsAsync(db, fx.Competition.Id);
        var (appeal, error) = await objections.FileAsync(fx.Applications[1].Id, null, Reason, RecruitmentAppealFiledBy.Candidate, null);

        Assert.True(appeal is not null, error);
        Assert.Equal(RecruitmentAppealKind.ResultObjection, appeal!.Kind);
        Assert.Equal(fx.Papers[fx.Applications[1].Id].Id, appeal.ObjectionExamCandidateId);
        Assert.NotNull(appeal.CompetitionResultVersionId);
        Assert.Equal(RecruitmentAppealStatus.Pending, (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == fx.Applications[1].Id)).AppealStatus);
        Assert.Equal(CompetitionObjectionService.PendingExistsMessage,
            (await objections.FileAsync(fx.Applications[1].Id, null, Reason, RecruitmentAppealFiledBy.HrOnBehalf, Creator)).Error);
        Assert.True(await objections.HasPendingAsync(fx.Competition.Id));
    }

    [Fact]
    public async Task An_objection_after_the_window_is_refused_AC_HCM_31e_negative()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [60m, 45m]);
        await PublishResultsAsync(db, fx.Competition.Id);
        var competition = await db.RecruitmentCompetitions.SingleAsync();
        competition.ResultsFirstPublishedAt = DateTime.UtcNow.AddDays(-(competition.ObjectionWindowDays + 1));
        await db.SaveChangesAsync();

        var (appeal, error) = await CreateObjections(db).FileAsync(fx.Applications[1].Id, null, Reason, RecruitmentAppealFiledBy.Candidate, null);

        Assert.Null(appeal);
        Assert.StartsWith("انتهت مدة الاعتراض في ", error, StringComparison.Ordinal);
        Assert.Empty(await db.RecruitmentAppeals.ToListAsync());
    }

    [Fact]
    public async Task An_upheld_objection_corrects_the_mark_and_creates_a_new_results_version_with_a_diff_AC_HCM_31e()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (fx, appeal, reviewer) = await FileAndAssignAsync(db, [60m, 45m], objector: 1);
        var objections = CreateObjections(db);

        var (ok, error, notice) = await objections.DecideAsync(appeal.Id, RecruitmentAppealOutcome.ObjectionUpheld, Rationale, 75m,
            Hcm028AppealTestSupport.Reviewer(Guid.NewGuid(), reviewer.Id));

        Assert.True(ok, error);
        Assert.Null(notice);
        var decided = await db.RecruitmentAppeals.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentAppealStatus.Decided, decided.Status);
        Assert.Equal(RecruitmentAppealOutcome.ObjectionUpheld, decided.Outcome);
        var paper = await db.ExamCandidates.AsNoTracking().SingleAsync(c => c.Id == appeal.ObjectionExamCandidateId);
        Assert.Equal(75m, paper.FinalMark);
        Assert.Single(await db.ExamScores.AsNoTracking().Where(s => s.Round == ExamScoreRound.ObjectionCorrection).ToListAsync());

        var draft = await db.CompetitionResultVersions.AsNoTracking().SingleAsync(v => v.VersionNo == 2);
        Assert.Equal(CompetitionResultVersionStatus.Draft, draft.Status);
        Assert.Contains("اعتراض مقبول", draft.Reason, StringComparison.Ordinal);
        var diff = JsonSerializer.Deserialize<List<CompetitionResultChange>>(draft.DiffJson!)!;
        var objectorChange = Assert.Single(diff, d => d.ExamNumber == paper.ExamNumber);
        Assert.Equal(2, objectorChange.FromRank);
        Assert.Equal(1, objectorChange.ToRank);
        Assert.Equal("Failed", objectorChange.FromOutcome);
        Assert.Equal("Passed", objectorChange.ToOutcome);

        // The new version goes through the results approval again, then supersedes the first.
        await ApproveAndPublishAsync(db, draft.Id);
        var versions = await db.CompetitionResultVersions.AsNoTracking().OrderBy(v => v.VersionNo).ToListAsync();
        Assert.Equal([CompetitionResultVersionStatus.Superseded, CompetitionResultVersionStatus.Published], versions.Select(v => v.Status));
        Assert.Equal(draft.Id, (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).CurrentResultVersionId);
    }

    [Fact]
    public async Task A_dismissed_objection_changes_nothing()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (_, appeal, reviewer) = await FileAndAssignAsync(db, [60m, 45m], objector: 1);

        var (ok, error, _) = await CreateObjections(db).DecideAsync(appeal.Id, RecruitmentAppealOutcome.ObjectionDismissed, Rationale, null,
            Hcm028AppealTestSupport.Reviewer(Guid.NewGuid(), reviewer.Id));

        Assert.True(ok, error);
        Assert.Single(await db.CompetitionResultVersions.ToListAsync());
        Assert.Empty(await db.ExamScores.Where(s => s.Round == ExamScoreRound.ObjectionCorrection).ToListAsync());
        Assert.False(await CreateObjections(db).HasPendingAsync((await db.RecruitmentCompetitions.SingleAsync()).Id));
    }

    [Fact]
    public async Task Objection_outcomes_need_a_corrected_mark_within_the_maximum_and_never_apply_to_rejection_appeals()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (_, appeal, reviewer) = await FileAndAssignAsync(db, [60m, 45m], objector: 1);
        var objections = CreateObjections(db);
        var actor = Hcm028AppealTestSupport.Reviewer(Guid.NewGuid(), reviewer.Id);

        Assert.NotNull((await objections.DecideAsync(appeal.Id, RecruitmentAppealOutcome.ObjectionUpheld, Rationale, null, actor)).Error);
        Assert.NotNull((await objections.DecideAsync(appeal.Id, RecruitmentAppealOutcome.ObjectionUpheld, Rationale, 101m, actor)).Error);
        Assert.NotNull((await objections.DecideAsync(appeal.Id, RecruitmentAppealOutcome.Upheld, Rationale, 70m, actor)).Error);
        Assert.NotNull(RecruitmentAppealWorkflowRules.ValidateOutcome(RecruitmentAppealOutcome.ObjectionUpheld, RequisitionStatus.Published, 1, null));
        Assert.Equal(RecruitmentAppealStatus.Pending, (await db.RecruitmentAppeals.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task A_later_version_does_not_reopen_the_objection_window()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (fx, appeal, reviewer) = await FileAndAssignAsync(db, [60m, 45m], objector: 1);
        var firstPublished = (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).ResultsFirstPublishedAt;
        await CreateObjections(db).DecideAsync(appeal.Id, RecruitmentAppealOutcome.ObjectionUpheld, Rationale, 75m,
            Hcm028AppealTestSupport.Reviewer(Guid.NewGuid(), reviewer.Id));
        var draft = await db.CompetitionResultVersions.SingleAsync(v => v.VersionNo == 2);

        await ApproveAndPublishAsync(db, draft.Id);

        var competition = await db.RecruitmentCompetitions.AsNoTracking().SingleAsync();
        Assert.Equal(firstPublished, competition.ResultsFirstPublishedAt);
        competition = await db.RecruitmentCompetitions.SingleAsync();
        competition.ResultsFirstPublishedAt = DateTime.UtcNow.AddDays(-(competition.ObjectionWindowDays + 1));
        await db.SaveChangesAsync();
        Assert.StartsWith("انتهت مدة الاعتراض",
            (await CreateObjections(db).FileAsync(fx.Applications[0].Id, null, Reason, RecruitmentAppealFiledBy.Candidate, null)).Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Markers_of_the_competition_cannot_review_an_objection()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [60m, 45m]);
        await PublishResultsAsync(db, fx.Competition.Id);
        var (appeal, _) = await CreateObjections(db).FileAsync(fx.Applications[1].Id, null, Reason, RecruitmentAppealFiledBy.Candidate, null);

        var excluded = await Hcm028AppealTestSupport.CreateService(db).GetNonIndependentEmployeeIdsAsync(appeal!, null);

        Assert.All(fx.Markers, m => Assert.Contains(m.EmployeeId!.Value, excluded));
        Assert.False((await Hcm028AppealTestSupport.CreateService(db).AssignReviewerAsync(appeal!.Id, fx.Markers[0].EmployeeId!.Value, Creator, null, "hr")).Success);
    }
}
