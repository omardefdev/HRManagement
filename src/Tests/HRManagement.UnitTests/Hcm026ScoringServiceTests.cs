using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class Hcm026ScoringServiceTests
{
    // A: weight 40, max 100 — B: weight 60, max 50.
    private static readonly AssessmentCriteriaItem[] Items =
    [
        new("A", "البند أ", 40m, 100m, AssessmentStage.Experience),
        new("B", "البند ب", 60m, 50m, AssessmentStage.Exam)
    ];

    private static readonly Guid Assessor1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Assessor2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    [Fact]
    public async Task Score_above_max_rejected()
    {
        var (db, app, _) = await SeedAsync();
        await using var _db = db;
        var service = CreateService(db);

        var (success, error) = await service.SubmitScoresAsync(app.Id, Assessor1, null, Scores(("A", 80m), ("B", 51m)), null, true);

        Assert.False(success);
        Assert.Contains("بين 0 و 50", error);
        Assert.False(await db.RecruitmentAssessmentScores.AnyAsync());
    }

    [Fact]
    public async Task Negative_score_rejected()
    {
        var (db, app, _) = await SeedAsync();
        await using var _db = db;

        var (success, _) = await CreateService(db).SubmitScoresAsync(app.Id, Assessor1, null, Scores(("A", -1m)), null, true);

        Assert.False(success);
    }

    [Fact]
    public async Task Unknown_key_rejected()
    {
        var (db, app, _) = await SeedAsync();
        await using var _db = db;

        var (success, error) = await CreateService(db).SubmitScoresAsync(app.Id, Assessor1, null, Scores(("Z", 1m)), null, true);

        Assert.False(success);
        Assert.Contains("Z", error);
    }

    [Fact]
    public async Task Coi_not_declared_rejected()
    {
        var (db, app, _) = await SeedAsync();
        await using var _db = db;

        var (success, error) = await CreateService(db).SubmitScoresAsync(app.Id, Assessor1, null, Scores(("A", 80m), ("B", 40m)), null, false);

        Assert.False(success);
        Assert.Contains("تعارض", error);
    }

    [Fact]
    public async Task Applicant_as_assessor_rejected()
    {
        var (db, app, _) = await SeedAsync();
        await using var _db = db;
        var applicantEmployee = await AddEmployeeAsync(db, "E-APP");
        await LinkInternalApplicationAsync(db, app, applicantEmployee.Id);

        var (success, error) = await CreateService(db).SubmitScoresAsync(
            app.Id, Assessor1, applicantEmployee.Id, Scores(("A", 80m), ("B", 40m)), null, true);

        Assert.False(success);
        Assert.Contains("تقييم طلبه", error);
    }

    [Fact]
    public async Task Conflicted_committee_member_rejected()
    {
        var (db, app, _) = await SeedAsync();
        await using var _db = db;
        var member = await AddEmployeeAsync(db, "E-COI");
        var session = new SelectionCommitteeSession
        {
            RequisitionId = app.RequisitionId!.Value,
            CommitteeDate = DateTime.UtcNow.Date,
        };
        db.SelectionCommitteeSessions.Add(session);
        db.SelectionCommitteeMembers.Add(new SelectionCommitteeMember
        {
            SessionId = session.Id,
            EmployeeId = member.Id,
            HasConflictOfInterest = true
        });
        await db.SaveChangesAsync();

        var (success, error) = await CreateService(db).SubmitScoresAsync(
            app.Id, Assessor1, member.Id, Scores(("A", 80m), ("B", 40m)), null, true);

        Assert.False(success);
        Assert.Contains("تعارض", error);
    }

    [Fact]
    public async Task Weighted_score_is_exact_mean_of_assessors()
    {
        var (db, app, criteria) = await SeedAsync();
        await using var _db = db;
        var service = CreateService(db, minAssessors: 1);

        Assert.True((await service.SubmitScoresAsync(app.Id, Assessor1, null, Scores(("A", 80m), ("B", 40m)), "جيد", true)).Success);
        Assert.True((await service.SubmitScoresAsync(app.Id, Assessor2, null, Scores(("A", 60m), ("B", 25m)), null, true)).Success);

        db.ChangeTracker.Clear();
        var summary = await service.GetSummaryAsync(app.Id);
        Assert.Equal(80.00m, summary.Assessors.Single(a => a.AssessorUserId == Assessor1).WeightedTotal);
        Assert.Equal(54.00m, summary.Assessors.Single(a => a.AssessorUserId == Assessor2).WeightedTotal);
        Assert.Equal(67.00m, summary.FinalScore);
        Assert.Equal(2, summary.CompleteAssessors);

        var saved = await db.RecruitmentApplications.SingleAsync();
        Assert.Equal(67.00m, saved.AssessmentScore);
        Assert.Equal(criteria.Id, saved.CriteriaVersionId);
        Assert.Equal(RecruitmentApplicationStatus.Assessed, saved.Status);
        Assert.NotNull((await db.RecruitmentAssessmentCriteria.SingleAsync()).LockedAt);
    }

    [Fact]
    public async Task Resubmission_updates_existing_rows()
    {
        var (db, app, _) = await SeedAsync();
        await using var _db = db;
        var service = CreateService(db);

        await service.SubmitScoresAsync(app.Id, Assessor1, null, Scores(("A", 80m)), null, true);
        await service.SubmitScoresAsync(app.Id, Assessor1, null, Scores(("A", 70m), ("B", 50m)), null, true);

        Assert.Equal(2, await db.RecruitmentAssessmentScores.CountAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(88.00m, (await db.RecruitmentApplications.SingleAsync()).AssessmentScore);
    }

    [Fact]
    public async Task Incomplete_assessor_does_not_count()
    {
        var (db, app, _) = await SeedAsync();
        await using var _db = db;
        var service = CreateService(db);

        Assert.True((await service.SubmitScoresAsync(app.Id, Assessor1, null, Scores(("A", 80m)), null, true)).Success);

        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentApplications.SingleAsync();
        Assert.Null(saved.AssessmentScore);
        Assert.Equal(RecruitmentApplicationStatus.InterviewScheduled, saved.Status);
    }

    [Fact]
    public async Task MinAssessors_two_requires_two_complete_assessors()
    {
        var (db, app, _) = await SeedAsync();
        await using var _db = db;
        var service = CreateService(db, minAssessors: 2);

        await service.SubmitScoresAsync(app.Id, Assessor1, null, Scores(("A", 80m), ("B", 40m)), null, true);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.InterviewScheduled, (await db.RecruitmentApplications.SingleAsync()).Status);

        await service.SubmitScoresAsync(app.Id, Assessor2, null, Scores(("A", 60m), ("B", 25m)), null, true);
        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentApplications.SingleAsync();
        Assert.Equal(RecruitmentApplicationStatus.Assessed, saved.Status);
        Assert.Equal(67.00m, saved.AssessmentScore);
    }

    [Fact]
    public async Task Scoring_after_ranked_rejected()
    {
        var (db, app, _) = await SeedAsync(RecruitmentApplicationStatus.Ranked);
        await using var _db = db;

        var (success, error) = await CreateService(db).SubmitScoresAsync(
            app.Id, Assessor1, null, Scores(("A", 80m), ("B", 40m)), null, true);

        Assert.False(success);
        Assert.Contains("مُقفلة", error);
    }

    [Fact]
    public async Task Scoring_without_active_criteria_rejected()
    {
        var (db, app, criteria) = await SeedAsync();
        await using var _db = db;
        criteria.IsActive = false;
        await db.SaveChangesAsync();

        var (success, _) = await CreateService(db).SubmitScoresAsync(app.Id, Assessor1, null, Scores(("A", 80m)), null, true);

        Assert.False(success);
    }

    [Fact]
    public async Task Manual_transition_to_assessed_rejected()
    {
        var (db, app, criteria) = await SeedAsync();
        await using var _db = db;
        app.AssessmentScore = 90m;
        app.CriteriaVersionId = criteria.Id;
        await db.SaveChangesAsync();

        var (success, error) = await new RecruitmentSelectionService(db)
            .TransitionStatusAsync(app.Id, RecruitmentApplicationStatus.Assessed, legacyHireWithoutOffer: false);

        Assert.False(success);
        Assert.Equal(SelectionWorkflowRules.AssessedIsAutomaticMessage, error);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.InterviewScheduled, (await db.RecruitmentApplications.SingleAsync()).Status);
    }

    [Fact]
    public void Scoring_rules_compute_exact_example()
    {
        var a1 = AssessmentScoringRules.ComputeAssessor(Assessor1, Items, [new("A", 80m), new("B", 40m)]);
        var a2 = AssessmentScoringRules.ComputeAssessor(Assessor2, Items, [new("A", 60m), new("B", 25m)]);
        Assert.Equal(80.00m, a1.WeightedTotal);
        Assert.Equal(54.00m, a2.WeightedTotal);
        Assert.Equal(67.00m, AssessmentScoringRules.ComputeFinal(Items,
        [
            [new("A", 80m), new("B", 40m)],
            [new("A", 60m), new("B", 25m)]
        ]));
    }

    internal static Dictionary<string, decimal> Scores(params (string Key, decimal Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);

    internal static RecruitmentScoringService CreateService(HRCoreDbContext db, int minAssessors = 1)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions { MinAssessors = minAssessors });
        return new RecruitmentScoringService(db, new RecruitmentInterviewService(db, options), options);
    }

    internal static async Task<Employee> AddEmployeeAsync(HRCoreDbContext db, string number)
    {
        var employee = new Employee
        {
            NationalId = $"0101{Math.Abs(number.GetHashCode()) % 10_000_000:D7}",
            FirstName = "موظف",
            LastName = number,
            FullName = $"موظف {number}",
            BirthDate = new DateTime(1990, 1, 1),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    private static async Task LinkInternalApplicationAsync(HRCoreDbContext db, RecruitmentApplication app, Guid employeeId)
    {
        var vacancy = new InternalVacancy { Title = "شاغر داخلي", RequisitionId = app.RequisitionId, IsOpen = true };
        db.InternalVacancies.Add(vacancy);
        var internalApp = new InternalApplication
        {
            InternalVacancyId = vacancy.Id,
            EmployeeId = employeeId,
            RequisitionId = app.RequisitionId,
            AppliedAt = DateTime.UtcNow,
            Status = InternalApplicationStatus.UnderReview
        };
        db.InternalApplications.Add(internalApp);
        app.SourceInternalApplicationId = internalApp.Id;
        await db.SaveChangesAsync();
    }

    internal static async Task<(HRCoreDbContext Db, RecruitmentApplication App, RecruitmentAssessmentCriteria Criteria)> SeedAsync(
        RecruitmentApplicationStatus status = RecruitmentApplicationStatus.InterviewScheduled,
        IReadOnlyList<AssessmentCriteriaItem>? items = null)
    {
        var db = Hcm026CriteriaServiceTests.CreateContext();
        var req = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Published);
        var criteria = new RecruitmentAssessmentCriteria
        {
            RequisitionId = req.Id,
            VersionNumber = 1,
            Title = "v1",
            CriteriaJson = AssessmentCriteriaSerializer.Serialize(items ?? Items),
            IsActive = true
        };
        db.RecruitmentAssessmentCriteria.Add(criteria);
        var app = new RecruitmentApplication
        {
            ApplicantFullName = "مرشح",
            AppliedPosition = "محلل",
            RequisitionId = req.Id,
            AppliedAt = DateTime.UtcNow,
            Status = status,
            ConflictOfInterestDeclared = true
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();
        return (db, app, criteria);
    }
}
