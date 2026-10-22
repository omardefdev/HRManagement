using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>HCM-028 session 2: SQLite fixtures for rejection tracking, the appeal lifecycle and the hire block.</summary>
internal static class Hcm028AppealTestSupport
{
    /// <summary>Central HR (files, assigns, resubmits).</summary>
    public static readonly Guid HrUserId = Guid.Parse("28280000-0000-4000-8000-0000000000a1");
    public static readonly Guid ReviewerUserId = Guid.Parse("28280000-0000-4000-8000-0000000000c1");
    public static readonly Guid OtherComplianceUserId = Guid.Parse("28280000-0000-4000-8000-0000000000c2");

    public const string Reason = "المرشح يعترض على احتساب درجة الخبرة في التقييم";
    public const string Rationale = "ثبت للمراجع خطأ في احتساب درجة الخبرة وفق المعايير المعتمدة";

    public static RecruitmentAppealService CreateService(
        HRCoreDbContext db,
        IConfiguration? config = null,
        RecruitmentOptions? options = null,
        IWorkflowStarter? starter = null) =>
        new(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            starter ?? Hcm027TestSupport.CreateStarter(db),
            config ?? Hcm027TestSupport.Config(),
            Microsoft.Extensions.Options.Options.Create(options ?? new RecruitmentOptions()),
            Hcm027TestSupport.CreateActionService(db),
            new SelectionCommitteeService(db),
            new RecruitmentAppealWorkflowHandler(db));

    public static AppealReviewActor Reviewer(Guid userId, Guid? employeeId, string role = HrManagementRoles.Compliance) =>
        new(
            new WorkflowActionActor(userId, $"user-{userId:N}"[..12], role),
            employeeId,
            role is HrManagementRoles.Compliance or HrManagementRoles.Admin);

    /// <summary>Published requisition (WorkflowPackSeeder included) with one Ranked application rejected by selection.</summary>
    public static async Task<(RecruitmentRequisition Requisition, RecruitmentApplication Application)> SeedRejectedAsync(
        HRCoreDbContext db,
        string suffix = "AP",
        int? rank = 1)
    {
        await WorkflowPackSeeder.SeedAsync(db);
        var req = await Hcm027TestSupport.SeedRequisitionAsync(db, $"H28{suffix}");
        var app = await Hcm027TestSupport.SeedApplicationAsync(
            db, req, rank is null ? RecruitmentApplicationStatus.Assessed : RecruitmentApplicationStatus.Ranked, rank, "مستأنِف");
        app.ConflictOfInterestDeclared = true;
        await db.SaveChangesAsync();
        var (ok, error) = await new RecruitmentSelectionService(db)
            .TransitionStatusAsync(app.Id, RecruitmentApplicationStatus.Rejected, false, "لم يستوفِ شرط الخبرة");
        Assert.True(ok, error);
        return (req, app);
    }

    public static async Task<RecruitmentAppeal> FileAsync(RecruitmentAppealService service, RecruitmentApplication app)
    {
        var (appeal, error) = await service.FileAppealAsync(app.Id, Reason, RecruitmentAppealFiledBy.HrOnBehalf, HrUserId);
        Assert.True(appeal is not null, error);
        return appeal!;
    }

    /// <summary>Files, seeds the reviewer employee and assigns them (starting the review workflow).</summary>
    public static async Task<(RecruitmentAppeal Appeal, Employee ReviewerEmployee)> FileAndAssignAsync(
        HRCoreDbContext db,
        RecruitmentAppealService service,
        RecruitmentApplication app)
    {
        var appeal = await FileAsync(service, app);
        var reviewer = await Hcm028TestSupport.SeedEmployeeAsync(db, "مراجع الامتثال");
        var (ok, error) = await service.AssignReviewerAsync(appeal.Id, reviewer.Id, HrUserId);
        Assert.True(ok, error);
        return (appeal, reviewer);
    }

    public static async Task<RecruitmentAppeal> ReloadAsync(HRCoreDbContext db, Guid appealId)
    {
        db.ChangeTracker.Clear();
        return await db.RecruitmentAppeals.AsNoTracking()
            .Include(a => a.RecruitmentApplication)
            .SingleAsync(a => a.Id == appealId);
    }
}
