using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using static HRManagement.UnitTests.Hcm027TestSupport;
using static HRManagement.UnitTests.Hcm028AppealTestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-028 (I): no hire while any application on the requisition has a pending appeal (configurable).</summary>
public class Hcm028HireBlockTests
{
    private static IConfiguration Config(bool block) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:RejectSiblingApplicationsOnHire"] = "true",
                ["Recruitment:LegacyHireWithoutOffer"] = "false",
                ["Recruitment:BlockHireOnPendingAppeal"] = block.ToString()
            })
            .Build();

    private static async Task<(HR.Core.Domain.Entities.RecruitmentApplication Winner, HR.Core.Domain.Entities.RecruitmentAppeal Appeal)>
        SeedWinnerWithPendingSiblingAppealAsync(HR.Core.Infrastructure.Data.HRCoreDbContext db)
    {
        await WorkflowPackSeeder.SeedAsync(db);
        var (winner, req) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var appellant = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2, "مستأنِف");
        var (ok, error) = await new RecruitmentSelectionService(db)
            .TransitionStatusAsync(appellant.Id, RecruitmentApplicationStatus.Rejected, false, "رفض الاختيار");
        Assert.True(ok, error);
        var appeal = await FileAsync(CreateService(db), appellant);
        return (winner, appeal);
    }

    [Fact]
    public async Task Hire_is_refused_while_a_sibling_appeal_is_pending()
    {
        await using var db = CreateContext();
        var (winner, _) = await SeedWinnerWithPendingSiblingAppealAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Hcm027ReserveListTests.HireAsync(db, winner, Config(block: true)));

        Assert.Equal("يوجد استئناف معلق على هذا الشاغر — يجب البت فيه قبل التعيين.", ex.Message);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.OfferAccepted, (await db.RecruitmentApplications.SingleAsync(a => a.Id == winner.Id)).Status);
    }

    [Fact]
    public async Task Hire_is_allowed_after_the_appeal_is_decided()
    {
        await using var db = CreateContext();
        var (winner, appeal) = await SeedWinnerWithPendingSiblingAppealAsync(db);
        var service = CreateService(db);
        var reviewer = await Hcm028TestSupport.SeedEmployeeAsync(db, "مراجع");
        Assert.True((await service.AssignReviewerAsync(appeal.Id, reviewer.Id, HrUserId)).Success);
        var (decided, error) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Dismissed, Rationale, null, Reviewer(ReviewerUserId, reviewer.Id));
        Assert.True(decided, error);

        await Hcm027ReserveListTests.HireAsync(db, winner, Config(block: true));

        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.Hired, (await db.RecruitmentApplications.SingleAsync(a => a.Id == winner.Id)).Status);
    }

    [Fact]
    public async Task Hire_is_allowed_when_the_option_is_off()
    {
        await using var db = CreateContext();
        var (winner, _) = await SeedWinnerWithPendingSiblingAppealAsync(db);

        await Hcm027ReserveListTests.HireAsync(db, winner, Config(block: false));

        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.Hired, (await db.RecruitmentApplications.SingleAsync(a => a.Id == winner.Id)).Status);
    }

    [Fact]
    public void Block_is_on_by_default() => Assert.True(new RecruitmentOptions().BlockHireOnPendingAppeal);

    [Fact]
    public async Task Guard_reports_only_pending_appeals_on_the_same_requisition()
    {
        await using var db = CreateContext();
        var (winner, _) = await SeedWinnerWithPendingSiblingAppealAsync(db);
        var other = await SeedRequisitionAsync(db, "HBO");

        Assert.Equal(RecruitmentAppealWorkflowRules.HireBlockedMessage,
            await RecruitmentHireAppealGuard.ValidateAsync(db, winner.RequisitionId, blockHireOnPendingAppeal: true));
        Assert.Null(await RecruitmentHireAppealGuard.ValidateAsync(db, other.Id, blockHireOnPendingAppeal: true));
        Assert.Null(await RecruitmentHireAppealGuard.ValidateAsync(db, winner.RequisitionId, blockHireOnPendingAppeal: false));
    }
}
