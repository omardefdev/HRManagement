using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

public class Hcm026IntakeCloserTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Closes_expired_and_keeps_requisition_published()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var fx = await Hcm026RequisitionIntegrityTests.SeedPublishedPipelineAsync(db);
        fx.Requisition.ApplicationClosingDate = Today.AddDays(-1);
        await db.SaveChangesAsync();

        var closed = await CreateCloser(db).CloseExpiredAsync(Today, CancellationToken.None);

        Assert.Equal(1, closed);
        db.ChangeTracker.Clear();
        var req = await db.RecruitmentRequisitions.SingleAsync();
        Assert.Equal(RequisitionStatus.Published, req.Status);
        Assert.NotNull(req.IntakeClosedAt);
        Assert.False((await db.PublicJobPostings.SingleAsync()).IsPublished);
        Assert.False((await db.InternalVacancies.SingleAsync()).IsOpen);
        // Selection continues: open applications are untouched.
        Assert.Equal(RecruitmentApplicationStatus.Submitted,
            (await db.RecruitmentApplications.SingleAsync(a => a.Id == fx.SubmittedApp)).Status);
    }

    [Fact]
    public async Task Ignores_future_today_and_undated()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var future = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Published);
        future.ApplicationClosingDate = Today.AddDays(5);
        var today = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Published);
        today.ApplicationClosingDate = Today; // closing date is inclusive
        var undated = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Published);
        undated.ApplicationClosingDate = null;
        var draft = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Draft);
        draft.ApplicationClosingDate = Today.AddDays(-10);
        await db.SaveChangesAsync();

        Assert.Equal(0, await CreateCloser(db).CloseExpiredAsync(Today, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.All(await db.RecruitmentRequisitions.ToListAsync(), r => Assert.Null(r.IntakeClosedAt));
    }

    [Fact]
    public async Task Is_idempotent()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var fx = await Hcm026RequisitionIntegrityTests.SeedPublishedPipelineAsync(db);
        fx.Requisition.ApplicationClosingDate = Today.AddDays(-3);
        await db.SaveChangesAsync();
        var closer = CreateCloser(db);

        Assert.Equal(1, await closer.CloseExpiredAsync(Today, CancellationToken.None));
        db.ChangeTracker.Clear();
        var firstClosedAt = (await db.RecruitmentRequisitions.SingleAsync()).IntakeClosedAt;

        Assert.Equal(0, await closer.CloseExpiredAsync(Today, CancellationToken.None));
        db.ChangeTracker.Clear();
        var req = await db.RecruitmentRequisitions.SingleAsync();
        Assert.Equal(firstClosedAt, req.IntakeClosedAt);
        Assert.Equal(RequisitionStatus.Published, req.Status);
    }

    private static RecruitmentIntakeCloser CreateCloser(HR.Core.Infrastructure.Data.HRCoreDbContext db) =>
        new(db, NullLogger<RecruitmentIntakeCloser>.Instance);
}
