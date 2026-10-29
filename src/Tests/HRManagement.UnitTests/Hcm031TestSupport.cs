using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>HCM-031 (PLAN-31): competition fixtures (SQLite in memory, see <see cref="Hcm027TestSupport.CreateContext"/>).</summary>
internal static class Hcm031TestSupport
{
    public static readonly Guid Creator = Guid.Parse("31310000-0000-4000-8000-000000000001");
    public static readonly Guid Reviewer = Guid.Parse("31310000-0000-4000-8000-000000000002");
    public static readonly Guid Approver = Guid.Parse("31310000-0000-4000-8000-000000000003");

    private static int _counter;

    /// <summary>Acceptance-only legal inputs (test data, not legal values).</summary>
    public static CompetitionLegalInputs LegalInputs(int maxLines = 2) => new(
        ObjectionWindowDays: 7,
        ReserveValidityMonths: 6,
        MaxLinesPerCandidate: maxLines,
        PassMark: 50m,
        ReconcileThreshold: 10m,
        ReconcileRule: CompetitionReconcileRule.ReconcilerDecides,
        TieBreakOrder: [CompetitionTieBreak.ExamScore, CompetitionTieBreak.AppliedAt]);

    public static CompetitionInput Input(string title = "مسابقة التعيين 2026", int maxLines = 2) => new(
        title,
        Syr001LegislationIds.Law50_2004,
        "شروط التقديم وفق النظام",
        RecruitmentPublishingChannel.PublicOnly,
        null,
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
        LegalInputs(maxLines));

    /// <summary>Exam 70 / interview 30 — the one criteria shape shared by every line of a competition (J2).</summary>
    public static IReadOnlyList<AssessmentCriteriaItem> ExamCriteria(decimal examWeight = 70m) =>
    [
        new("EXAM", "الامتحان الكتابي", examWeight, 100m, AssessmentStage.Exam),
        new("INT", "المقابلة", 100m - examWeight, 10m, AssessmentStage.Interview)
    ];

    public static CompetitionLifecycleService CreateLifecycle(HRCoreDbContext db, RecruitmentOptions? options = null)
    {
        var codes = new Mock<ICodeGenerator>();
        codes.Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CodeGenerationResult { Code = $"CMP-2026-{Interlocked.Increment(ref _counter):00000}" });
        return new CompetitionLifecycleService(
            db,
            codes.Object,
            new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db)),
            Hcm027TestSupport.CreateStarter(db),
            Microsoft.Extensions.Options.Options.Create(options ?? new RecruitmentOptions()));
    }

    public static WorkflowInstanceActionService CreateActionService(HRCoreDbContext db) =>
        new(
            db,
            new WorkflowEngine(),
            Mock.Of<IWorkflowSignatureService>(),
            Mock.Of<MassTransit.IPublishEndpoint>(),
            new RecruitmentRequisitionWorkflowHandler(db),
            new OfferLetterWorkflowHandler(db),
            new SelectionCommitteeWorkflowHandler(db),
            new RecruitmentAppealWorkflowHandler(db),
            new EstablishmentChangeWorkflowHandler(db, null!),
            new CompetitionWorkflowHandler(db));

    public static WorkflowActionActor Actor(Guid userId, string role = "hr-core") => new(userId, $"user-{userId:N}", role);

    /// <summary>An Approved requisition on a vacant funded position of <paramref name="grade"/>, with an active criteria version.</summary>
    public static async Task<RecruitmentRequisition> SeedApprovedRequisitionAsync(
        HRCoreDbContext db,
        string grade = "G5",
        IReadOnlyList<AssessmentCriteriaItem>? criteria = null,
        Guid? jobCatalogEntryId = null,
        RequisitionStatus status = RequisitionStatus.Approved)
    {
        if (!await db.LegislationSources.AnyAsync())
            await LegislationRegisterSeeder.SeedAsync(db);

        var n = Interlocked.Increment(ref _counter);
        var org = new OrgUnit { Code = $"OU-H31-{n}", NameAr = $"وحدة {n}", NameEn = $"Unit {n}" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = $"POS-H31-{n}",
            GradeCode = grade,
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id,
            JobCatalogEntryId = jobCatalogEntryId
        };
        db.FundedPositions.Add(position);
        var requisition = new RecruitmentRequisition
        {
            RequisitionNumber = $"REQ-H31-{n:00000}",
            FundedPositionId = position.Id,
            JobTitle = "محاسب",
            Status = status,
            ApprovedDate = DateTime.UtcNow,
            VacancyCount = 1,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.RecruitmentRequisitions.Add(requisition);
        db.RecruitmentAssessmentCriteria.Add(new RecruitmentAssessmentCriteria
        {
            RequisitionId = requisition.Id,
            Title = "معايير المسابقة",
            CriteriaJson = AssessmentCriteriaSerializer.Serialize(criteria ?? ExamCriteria()),
            IsActive = true
        });
        await db.SaveChangesAsync();
        return requisition;
    }

    /// <summary>
    /// Draft competition with lines of <paramref name="vacanciesPerLine"/> attached requisitions each (default 3 + 2,
    /// AC-HCM-31a), created by <see cref="Creator"/>.
    /// </summary>
    public static Task<RecruitmentCompetition> SeedDraftCompetitionAsync(
        HRCoreDbContext db,
        CompetitionLifecycleService lifecycle,
        params int[] vacanciesPerLine) =>
        SeedDraftCompetitionWithCriteriaAsync(db, lifecycle, null, vacanciesPerLine);

    public static async Task<RecruitmentCompetition> SeedDraftCompetitionWithCriteriaAsync(
        HRCoreDbContext db,
        CompetitionLifecycleService lifecycle,
        IReadOnlyList<AssessmentCriteriaItem>? criteria,
        params int[] vacanciesPerLine)
    {
        if (vacanciesPerLine.Length == 0)
            vacanciesPerLine = [3, 2];
        await Hcm027TestSupport.SeedReferenceDataAsync(db);

        var (competition, error) = await lifecycle.CreateAsync(Input(maxLines: Math.Min(2, vacanciesPerLine.Length)), Creator);
        Assert.Null(error);
        competition!.CreatedBy = Creator;
        await db.SaveChangesAsync();

        for (var i = 0; i < vacanciesPerLine.Length; i++)
        {
            var grade = $"G{i + 5}";
            var (line, lineError) = await lifecycle.AddLineAsync(
                competition.Id, new CompetitionLineInput($"السطر {i + 1}", grade, null, null, null), Creator);
            Assert.Null(lineError);
            for (var v = 0; v < vacanciesPerLine[i]; v++)
            {
                var requisition = await SeedApprovedRequisitionAsync(db, grade, criteria);
                var (attached, attachError) = await lifecycle.AttachRequisitionAsync(line!.Id, requisition.Id, Creator);
                Assert.True(attached, attachError);
            }
        }
        return competition;
    }

    /// <summary>Submits, approves both workflow steps (two people other than the creator) and announces.</summary>
    public static Task<RecruitmentCompetition> SeedAnnouncedCompetitionAsync(HRCoreDbContext db, params int[] vacanciesPerLine) =>
        SeedAnnouncedCompetitionWithCriteriaAsync(db, null, vacanciesPerLine);

    public static async Task<RecruitmentCompetition> SeedAnnouncedCompetitionWithCriteriaAsync(
        HRCoreDbContext db, IReadOnlyList<AssessmentCriteriaItem>? criteria, params int[] vacanciesPerLine)
    {
        var lifecycle = CreateLifecycle(db);
        var competition = await SeedDraftCompetitionWithCriteriaAsync(db, lifecycle, criteria, vacanciesPerLine);
        await ApproveAsync(db, lifecycle, competition.Id);
        var (announced, error) = await lifecycle.AnnounceAsync(competition.Id, Creator);
        Assert.True(announced, error);
        return await db.RecruitmentCompetitions.Include(c => c.Lines).SingleAsync(c => c.Id == competition.Id);
    }

    public static ExamSeatingService CreateSeating(HRCoreDbContext db) =>
        new(db, new ExamAdmissionCardDocumentService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())));

    /// <summary>
    /// Announced competition with intake closed and <paramref name="applicants"/> applications admitted to the exam
    /// (Shortlisted, seeded directly — test data), plus written sessions of the given capacities (max score 100).
    /// </summary>
    public static Task<(RecruitmentCompetition Competition, IReadOnlyList<ExamSession> Sessions)> SeedExamReadyAsync(
        HRCoreDbContext db, int applicants, params int[] capacities) =>
        SeedExamReadyWithCriteriaAsync(db, null, applicants, capacities);

    public static async Task<(RecruitmentCompetition Competition, IReadOnlyList<ExamSession> Sessions)> SeedExamReadyWithCriteriaAsync(
        HRCoreDbContext db, IReadOnlyList<AssessmentCriteriaItem>? criteria, int applicants, params int[] capacities)
    {
        var competition = await SeedAnnouncedCompetitionWithCriteriaAsync(db, criteria, 3, 2);
        Assert.True((await CreateLifecycle(db).CloseIntakeAsync(competition.Id, Creator)).Success);
        await SeedAdmittedApplicationsAsync(db, competition, applicants);
        var seating = CreateSeating(db);
        var sessions = new List<ExamSession>();
        for (var i = 0; i < capacities.Length; i++)
        {
            var (session, error) = await seating.CreateSessionAsync(competition.Id,
                new ExamSessionInput(ExamSessionType.Written, $"القاعة {i + 1}", DateTime.UtcNow.AddDays(10 + i), 120, $"المبنى أ — القاعة {i + 1}", capacities[i], 100m),
                Creator);
            Assert.Null(error);
            sessions.Add(session!);
        }
        return (await db.RecruitmentCompetitions.SingleAsync(c => c.Id == competition.Id), sessions);
    }

    public static async Task<IReadOnlyList<RecruitmentApplication>> SeedAdmittedApplicationsAsync(
        HRCoreDbContext db, RecruitmentCompetition competition, int count, RecruitmentApplicationStatus status = RecruitmentApplicationStatus.Shortlisted)
    {
        var lines = await db.CompetitionLines.Where(l => l.CompetitionId == competition.Id).OrderBy(l => l.LineNo).ToListAsync();
        var applications = new List<RecruitmentApplication>();
        for (var i = 0; i < count; i++)
        {
            var nationalId = $"H31-{competition.Number}-{Interlocked.Increment(ref _counter):D7}";
            var application = new RecruitmentApplication
            {
                TenantId = competition.TenantId,
                ApplicantFullName = $"متقدم {i + 1}",
                NationalId = nationalId,
                NationalIdHash = HRManagement.SharedKernel.PiiMask.Sha256Hex(nationalId),
                CompetitionId = competition.Id,
                AppliedPosition = competition.Title,
                AppliedAt = DateTime.UtcNow.AddMinutes(-count + i),
                Status = status,
                ConflictOfInterestDeclared = true,
                IntakeChannel = RecruitmentIntakeChannel.Public
            };
            applications.Add(application);
            for (var p = 0; p < lines.Count; p++)
                db.CompetitionPreferences.Add(new CompetitionPreference
                {
                    TenantId = competition.TenantId,
                    RecruitmentApplicationId = application.Id,
                    CompetitionLineId = lines[(i + p) % lines.Count].Id,
                    Order = p + 1
                });
        }
        db.RecruitmentApplications.AddRange(applications);
        await db.SaveChangesAsync();
        return applications;
    }

    /// <summary>Exam only (weight 100) — results need no non-exam assessment.</summary>
    public static IReadOnlyList<AssessmentCriteriaItem> ExamOnlyCriteria() =>
        [new("EXAM", "الامتحان الكتابي", 100m, 100m, AssessmentStage.Exam)];

    public sealed record MarkedCompetition(
        RecruitmentCompetition Competition,
        IReadOnlyList<RecruitmentApplication> Applications,
        IReadOnlyDictionary<Guid, ExamCandidate> Papers,
        ExamSession Session,
        IReadOnlyList<ExamMarkerActor> Markers);

    /// <summary>
    /// A competition (lines 3 + 2) whose single written session (max 100) is marked and closed: applicant i gets
    /// <paramref name="marks"/>[i] from both markers (null = absent). Applications are in application-date order.
    /// </summary>
    public static async Task<MarkedCompetition> SeedMarkedAsync(
        HRCoreDbContext db, IReadOnlyList<decimal?> marks, IReadOnlyList<AssessmentCriteriaItem>? criteria = null)
    {
        var (competition, sessions) = await SeedExamReadyWithCriteriaAsync(db, criteria ?? ExamOnlyCriteria(), 0, marks.Count);
        var applications = await SeedAdmittedApplicationsAsync(db, competition, marks.Count);
        var seating = CreateSeating(db);
        var seated = await seating.SeatAsync(competition.Id, Creator);
        Assert.True(seated.Success, seated.Error);
        var session = sessions[0];
        var papers = await db.ExamCandidates.Where(c => c.ExamSessionId == session.Id).ToDictionaryAsync(c => c.RecruitmentApplicationId);
        var attendance = applications.Select((a, i) => (Paper: papers[a.Id].Id, Present: marks[i] is not null))
            .ToDictionary(x => x.Paper, x => x.Present ? ExamAttendance.Present : ExamAttendance.Absent);
        Assert.True((await seating.SetAttendanceAsync(session.Id, attendance, Creator)).Success);
        Assert.True((await seating.MarkHeldAsync(session.Id, Creator)).Success);

        var markers = new List<ExamMarkerActor>();
        foreach (var role in new[] { ExamMarkerRole.First, ExamMarkerRole.Second, ExamMarkerRole.Reconciler })
        {
            var employee = await SeedEmployeeAsync(db, $"مصحح {role}");
            Assert.True((await seating.AssignMarkerAsync(session.Id, employee.Id, role, Creator)).Success);
            markers.Add(new ExamMarkerActor(Guid.NewGuid(), employee.Id, false));
        }
        var marking = new ExamMarkingService(db);
        for (var i = 0; i < applications.Count; i++)
        {
            if (marks[i] is not decimal mark)
                continue;
            Assert.True((await marking.SubmitAsync(papers[applications[i].Id].Id, mark, null, markers[0])).Success);
            Assert.True((await marking.SubmitAsync(papers[applications[i].Id].Id, mark, null, markers[1])).Success);
        }
        var (closed, error) = await marking.CloseMarkingAsync(session.Id, Creator);
        Assert.True(closed, error);
        return new MarkedCompetition(
            await db.RecruitmentCompetitions.Include(c => c.Lines).SingleAsync(c => c.Id == competition.Id),
            applications,
            await db.ExamCandidates.AsNoTracking().Where(c => c.ExamSessionId == session.Id).ToDictionaryAsync(c => c.RecruitmentApplicationId),
            session,
            markers);
    }

    public static CompetitionResultsService CreateResults(HRCoreDbContext db) => new(db, Hcm027TestSupport.CreateStarter(db));

    /// <summary>Computes, approves (committee step then HR director, two people other than the computer) and publishes.</summary>
    public static async Task<CompetitionResultVersion> PublishResultsAsync(HRCoreDbContext db, Guid competitionId, string? reason = null)
    {
        var results = CreateResults(db);
        var (version, error) = await results.ComputeDraftAsync(competitionId, reason, Creator);
        Assert.True(version is not null, error);
        await ApproveAndPublishAsync(db, version!.Id);
        return await db.CompetitionResultVersions.SingleAsync(v => v.Id == version.Id);
    }

    public static async Task ApproveAndPublishAsync(HRCoreDbContext db, Guid versionId)
    {
        var results = CreateResults(db);
        var (submitted, submitError) = await results.SubmitForApprovalAsync(versionId, Creator, "creator");
        Assert.True(submitted, submitError);
        var instanceId = await db.CompetitionResultVersions.Where(v => v.Id == versionId).Select(v => v.WorkflowInstanceId!.Value).SingleAsync();
        var actions = CreateActionService(db);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Reviewer), null, null)).Success);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Approver, "hr-management-approver"), null, null)).Success);
        var (published, publishError) = await results.PublishAsync(versionId, Creator);
        Assert.True(published, publishError);
    }

    public static async Task<Employee> SeedEmployeeAsync(HRCoreDbContext db, string name)
    {
        var employee = new Employee
        {
            FullName = name,
            NationalId = $"9{Interlocked.Increment(ref _counter):D10}",
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.Active,
            BirthDate = new DateTime(1985, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    public static async Task ApproveAsync(HRCoreDbContext db, CompetitionLifecycleService lifecycle, Guid competitionId)
    {
        var (submitted, error) = await lifecycle.SubmitForApprovalAsync(competitionId, Creator, "creator");
        Assert.True(submitted, error);
        var instanceId = await db.RecruitmentCompetitions.Where(c => c.Id == competitionId)
            .Select(c => c.ApprovalWorkflowInstanceId!.Value).SingleAsync();
        var actions = CreateActionService(db);
        var step1 = await actions.ApproveAsync(instanceId, Actor(Reviewer), null, null);
        Assert.True(step1.Success, step1.Error);
        var step2 = await actions.ApproveAsync(instanceId, Actor(Approver, "hr-management-approver"), null, null);
        Assert.True(step2.Success, step2.Error);
    }
}
