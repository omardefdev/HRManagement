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
    public static async Task<RecruitmentCompetition> SeedDraftCompetitionAsync(
        HRCoreDbContext db,
        CompetitionLifecycleService lifecycle,
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
                var requisition = await SeedApprovedRequisitionAsync(db, grade);
                var (attached, attachError) = await lifecycle.AttachRequisitionAsync(line!.Id, requisition.Id, Creator);
                Assert.True(attached, attachError);
            }
        }
        return competition;
    }

    /// <summary>Submits, approves both workflow steps (two people other than the creator) and announces.</summary>
    public static async Task<RecruitmentCompetition> SeedAnnouncedCompetitionAsync(HRCoreDbContext db, params int[] vacanciesPerLine)
    {
        var lifecycle = CreateLifecycle(db);
        var competition = await SeedDraftCompetitionAsync(db, lifecycle, vacanciesPerLine);
        await ApproveAsync(db, lifecycle, competition.Id);
        var (announced, error) = await lifecycle.AnnounceAsync(competition.Id, Creator);
        Assert.True(announced, error);
        return await db.RecruitmentCompetitions.Include(c => c.Lines).SingleAsync(c => c.Id == competition.Id);
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
