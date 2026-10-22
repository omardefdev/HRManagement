using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>HCM-030 (A, G-16): public and internal applications enter the recruitment path at intake.</summary>
public class Hcm030UnifiedIntakeTests
{
    [Fact]
    public async Task Public_apply_creates_citizen_and_recruitment_application_in_one_save()
    {
        var (db, saves) = Hcm030TestSupport.CreateContext();
        await using var _ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);
        var before = saves.Count;

        var result = await controller.Apply(Hcm030TestSupport.ApplyModel(fx.Posting.Id));

        Assert.Equal("Submitted", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(1, saves.Count - before);
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();
        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync();
        Assert.Equal(app.Id, citizen.PromotedRecruitmentApplicationId);
        Assert.Equal(RecruitmentApplicationStatus.Submitted, app.Status);
        Assert.Equal(RecruitmentIntakeChannel.Public, app.IntakeChannel);
        Assert.Equal(Hcm030TestSupport.TenantA, citizen.TenantId);
        Assert.Equal(Hcm030TestSupport.TenantA, app.TenantId);
        Assert.Equal(fx.Requisition.Id, app.RequisitionId);
        Assert.Equal(citizen.SubmittedAt, app.AppliedAt);
        Assert.Equal(citizen.NationalIdHash, app.NationalIdHash);
        Assert.Equal("محلل نظم", app.AppliedPosition);
        Assert.Contains(citizen.ReferenceNumber, app.Notes);
        Assert.False(app.ConflictOfInterestDeclared);
    }

    [Fact]
    public async Task Internal_apply_creates_internal_and_recruitment_application_in_one_transaction()
    {
        var (db, saves) = Hcm030TestSupport.CreateContext();
        await using var _ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var employee = await Hcm030TestSupport.SeedEmployeeAsync(db);
        var controller = Hcm030TestSupport.CreateEssController(db, employee.Id);
        var before = saves.Count;

        var result = await controller.Apply(fx.Vacancy.Id, "أرغب بالانتقال");

        Assert.Equal("MyApplications", Assert.IsType<RedirectToActionResult>(result).ActionName);
        // Two new rows that reference each other: both inserted, then the channel link (one transaction).
        Assert.Equal(2, saves.Count - before);
        var internalApp = await db.InternalApplications.AsNoTracking().SingleAsync();
        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync();
        Assert.Equal(app.Id, internalApp.PromotedRecruitmentApplicationId);
        Assert.Equal(internalApp.Id, app.SourceInternalApplicationId);
        Assert.Equal(RecruitmentApplicationStatus.Submitted, app.Status);
        Assert.Equal(RecruitmentIntakeChannel.Internal, app.IntakeChannel);
        Assert.Equal(Hcm030TestSupport.TenantA, app.TenantId);
        Assert.Equal(employee.NationalIdHash, app.NationalIdHash);
        Assert.Equal(internalApp.AppliedAt, app.AppliedAt);
    }

    [Fact]
    public async Task Legacy_posting_creates_only_the_citizen_row()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var posting = new PublicJobPosting { TenantId = Hcm030TestSupport.TenantA, Title = "قديم", IsPublished = true };
        db.PublicJobPostings.Add(posting);
        await db.SaveChangesAsync();

        await Hcm030TestSupport.CreatePublicCareersController(db).Apply(Hcm030TestSupport.ApplyModel(posting.Id));

        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();
        Assert.Null(citizen.PromotedRecruitmentApplicationId);
        Assert.Equal(CitizenApplicationStatus.Received, citizen.Status);
        Assert.Empty(await db.RecruitmentApplications.ToListAsync());
    }

    [Fact]
    public async Task Public_apply_is_refused_when_an_hr_entered_application_of_the_same_person_is_open()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        db.RecruitmentApplications.Add(new RecruitmentApplication
        {
            TenantId = Hcm030TestSupport.TenantA,
            ApplicantFullName = "مُدخل يدوياً",
            NationalIdHash = PiiMask.Sha256Hex("0101010101"),
            RequisitionId = fx.Requisition.Id,
            AppliedPosition = "محلل نظم",
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.UnderReview
        });
        await db.SaveChangesAsync();
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        var result = await controller.Apply(Hcm030TestSupport.ApplyModel(fx.Posting.Id));

        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("تم تقديم طلب سابق لنفس الوظيفة بهذا الرقم الوطني.", controller.TempData["Error"]);
        Assert.Empty(await db.CitizenApplications.ToListAsync());
        Assert.Single(await db.RecruitmentApplications.ToListAsync());
    }

    [Fact]
    public async Task Public_apply_is_allowed_when_the_other_application_is_terminal()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        db.RecruitmentApplications.Add(new RecruitmentApplication
        {
            TenantId = Hcm030TestSupport.TenantA,
            ApplicantFullName = "منسحب",
            NationalId = "0101010101",
            RequisitionId = fx.Requisition.Id,
            AppliedPosition = "محلل نظم",
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Withdrawn
        });
        await db.SaveChangesAsync();

        await Hcm030TestSupport.CreatePublicCareersController(db).Apply(Hcm030TestSupport.ApplyModel(fx.Posting.Id));

        Assert.Single(await db.CitizenApplications.ToListAsync());
        Assert.Equal(2, await db.RecruitmentApplications.CountAsync());
    }

    [Fact]
    public async Task Internal_apply_is_refused_when_the_employee_already_applied_publicly()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var employee = await Hcm030TestSupport.SeedEmployeeAsync(db, nationalId: "0202020202");
        await Hcm030TestSupport.CreatePublicCareersController(db).Apply(Hcm030TestSupport.ApplyModel(fx.Posting.Id, "0202020202"));
        var controller = Hcm030TestSupport.CreateEssController(db, employee.Id);

        await controller.Apply(fx.Vacancy.Id, null);

        Assert.Equal("سبق أن ترشّحت لهذا الشاغر.", controller.TempData["Error"]);
        Assert.Empty(await db.InternalApplications.ToListAsync());
        Assert.Equal(RecruitmentIntakeChannel.Public, (await db.RecruitmentApplications.SingleAsync()).IntakeChannel);
    }

    [Fact]
    public async Task Promote_on_a_linked_citizen_row_redirects_to_the_linked_application()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        await Hcm030TestSupport.CreatePublicCareersController(db).Apply(Hcm030TestSupport.ApplyModel(fx.Posting.Id));
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();
        var controller = Hcm029TestSupport.CreateHireController(db);

        var result = Assert.IsType<RedirectToActionResult>(await controller.PromoteFromCitizen(citizen.Id));

        Assert.Equal("Details", result.ActionName);
        Assert.Equal(citizen.PromotedRecruitmentApplicationId, result.RouteValues!["id"]);
        Assert.Equal(UnifiedIntakeService.ManagedByUnifiedPathMessage, controller.TempData["Info"]);
        Assert.Single(await db.RecruitmentApplications.ToListAsync());
    }

    [Fact]
    public async Task Promote_on_a_linked_internal_row_redirects_to_the_linked_application()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var employee = await Hcm030TestSupport.SeedEmployeeAsync(db);
        await Hcm030TestSupport.CreateEssController(db, employee.Id).Apply(fx.Vacancy.Id, null);
        var internalApp = await db.InternalApplications.AsNoTracking().SingleAsync();
        var controller = Hcm029TestSupport.CreateHireController(db);

        var result = Assert.IsType<RedirectToActionResult>(await controller.PromoteFromInternal(internalApp.Id));

        Assert.Equal("Details", result.ActionName);
        Assert.Equal(internalApp.PromotedRecruitmentApplicationId, result.RouteValues!["id"]);
        Assert.Single(await db.RecruitmentApplications.ToListAsync());
    }

    [Fact]
    public async Task Promote_on_an_unlinked_citizen_row_links_it_with_the_backfill_mapping()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var citizen = SeedUnlinkedCitizen(db, fx, CitizenApplicationStatus.Received);
        citizen.ConflictOfInterestDeclared = true;
        citizen.ConflictOfInterestDeclaredAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        var result = Assert.IsType<RedirectToActionResult>(
            await Hcm029TestSupport.CreateHireController(db).PromoteFromCitizen(citizen.Id));

        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync();
        Assert.Equal(app.Id, result.RouteValues!["id"]);
        Assert.Equal(RecruitmentApplicationStatus.Submitted, app.Status);
        Assert.Equal(RecruitmentIntakeChannel.Public, app.IntakeChannel);
        Assert.True(app.ConflictOfInterestDeclared);
        Assert.Equal(app.Id, (await db.CitizenApplications.AsNoTracking().SingleAsync()).PromotedRecruitmentApplicationId);
    }

    [Theory]
    [InlineData(CitizenApplicationStatus.Received, RequisitionStatus.Published, RecruitmentApplicationStatus.Submitted, null)]
    [InlineData(CitizenApplicationStatus.UnderReview, RequisitionStatus.Published, RecruitmentApplicationStatus.UnderReview, null)]
    [InlineData(CitizenApplicationStatus.Shortlisted, RequisitionStatus.Published, RecruitmentApplicationStatus.Shortlisted, null)]
    [InlineData(CitizenApplicationStatus.Accepted, RequisitionStatus.Published, RecruitmentApplicationStatus.Shortlisted, null)]
    [InlineData(CitizenApplicationStatus.Rejected, RequisitionStatus.Published, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.SelectionDecision)]
    [InlineData(CitizenApplicationStatus.Received, RequisitionStatus.Cancelled, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.RequisitionCancelled)]
    [InlineData(CitizenApplicationStatus.Shortlisted, RequisitionStatus.Closed, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.RequisitionClosed)]
    [InlineData(CitizenApplicationStatus.Rejected, RequisitionStatus.Cancelled, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.SelectionDecision)]
    public async Task EnsureLinked_maps_citizen_statuses(
        CitizenApplicationStatus source,
        RequisitionStatus requisitionStatus,
        RecruitmentApplicationStatus expected,
        RecruitmentRejectionKind? expectedKind)
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var endedAt = DateTime.UtcNow.AddDays(-40);
        fx.Requisition.Status = requisitionStatus;
        fx.Requisition.ClosedDate = requisitionStatus == RequisitionStatus.Closed ? endedAt : null;
        fx.Requisition.CancelledAt = requisitionStatus == RequisitionStatus.Cancelled ? endedAt : null;
        var citizen = SeedUnlinkedCitizen(db, fx, source);
        await db.SaveChangesAsync();
        var sourceUpdatedAt = DateTime.UtcNow.AddDays(-60);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"CitizenApplications\" SET \"UpdatedAt\" = {0} WHERE \"Id\" = {1}", sourceUpdatedAt, citizen.Id);
        await db.Entry(citizen).ReloadAsync();

        var app = await new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())).EnsureLinkedAsync(citizen);

        Assert.NotNull(app);
        Assert.Equal(expected, app.Status);
        Assert.Equal(expectedKind, app.RejectionKind);
        Assert.Equal(citizen.SubmittedAt, app.AppliedAt);
        if (expected == RecruitmentApplicationStatus.Rejected)
        {
            // Never "now": the rejection keeps the source (or requisition) date, so no appeal window reopens.
            var expectedRejectedAt = expectedKind == RecruitmentRejectionKind.SelectionDecision ? sourceUpdatedAt : endedAt;
            Assert.Equal(expectedRejectedAt, app.RejectedAt!.Value, TimeSpan.FromSeconds(1));
        }
        Assert.Equal(source == CitizenApplicationStatus.Accepted, app.Notes!.Contains(UnifiedIntakeStatusMapping.AcceptedNote));
        Assert.Equal(app.Id, citizen.PromotedRecruitmentApplicationId);
    }

    [Fact]
    public async Task EnsureLinked_is_idempotent_and_ignores_legacy_rows()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var citizen = SeedUnlinkedCitizen(db, fx, CitizenApplicationStatus.UnderReview);
        var legacy = SeedUnlinkedCitizen(db, fx, CitizenApplicationStatus.UnderReview, "CA-LEGACY");
        legacy.RequisitionId = null;
        await db.SaveChangesAsync();
        var service = new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));

        var first = await service.EnsureLinkedAsync(citizen);
        var second = await service.EnsureLinkedAsync(citizen);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Null(await service.EnsureLinkedAsync(legacy));
        Assert.Single(await db.RecruitmentApplications.ToListAsync());
    }

    [Fact]
    public async Task ChangeApplicationStatus_refuses_requisition_linked_citizen_rows()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        await Hcm030TestSupport.CreatePublicCareersController(db).Apply(Hcm030TestSupport.ApplyModel(fx.Posting.Id));
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();
        var controller = Hcm030TestSupport.CreatePublicJobPostingsController(db);

        await controller.ChangeApplicationStatus(citizen.Id, fx.Posting.Id, CitizenApplicationStatus.UnderReview);
        await controller.DeclareCitizenCoi(citizen.Id, fx.Posting.Id);

        Assert.Equal(UnifiedIntakeService.ManagedByUnifiedPathMessage, controller.TempData["Error"]);
        var reloaded = await db.CitizenApplications.AsNoTracking().SingleAsync();
        Assert.Equal(CitizenApplicationStatus.Received, reloaded.Status);
        Assert.False(reloaded.ConflictOfInterestDeclared);
    }

    [Fact]
    public async Task ChangeApplicationStatus_keeps_working_for_legacy_citizen_rows()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var posting = new PublicJobPosting { TenantId = Hcm030TestSupport.TenantA, Title = "قديم", IsPublished = true };
        db.PublicJobPostings.Add(posting);
        await db.SaveChangesAsync();
        await Hcm030TestSupport.CreatePublicCareersController(db).Apply(Hcm030TestSupport.ApplyModel(posting.Id));
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();

        await Hcm030TestSupport.CreatePublicJobPostingsController(db)
            .ChangeApplicationStatus(citizen.Id, posting.Id, CitizenApplicationStatus.UnderReview);

        Assert.Equal(CitizenApplicationStatus.UnderReview, (await db.CitizenApplications.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ChangeApplicationStatus_refuses_requisition_linked_internal_rows()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var employee = await Hcm030TestSupport.SeedEmployeeAsync(db);
        await Hcm030TestSupport.CreateEssController(db, employee.Id).Apply(fx.Vacancy.Id, null);
        var internalApp = await db.InternalApplications.AsNoTracking().SingleAsync();
        var controller = Hcm030TestSupport.CreateTalentMarketplaceController(db);

        await controller.ChangeApplicationStatus(internalApp.Id, fx.Vacancy.Id, InternalApplicationStatus.Shortlisted);

        Assert.Equal(UnifiedIntakeService.ManagedByUnifiedPathMessage, controller.TempData["Error"]);
        Assert.Equal(InternalApplicationStatus.Submitted, (await db.InternalApplications.AsNoTracking().SingleAsync()).Status);
    }

    private static CitizenApplication SeedUnlinkedCitizen(
        HR.Core.Infrastructure.Data.HRCoreDbContext db,
        Hcm030TestSupport.Fixture fx,
        CitizenApplicationStatus status,
        string reference = "CA-OLD-1")
    {
        var citizen = new CitizenApplication
        {
            TenantId = Hcm030TestSupport.TenantA,
            PublicJobPostingId = fx.Posting.Id,
            RequisitionId = fx.Requisition.Id,
            ReferenceNumber = reference,
            FullName = "مواطن قديم",
            NationalId = reference,
            NationalIdHash = PiiMask.Sha256Hex(reference),
            Status = status,
            SubmittedAt = DateTime.UtcNow.AddDays(-90)
        };
        db.CitizenApplications.Add(citizen);
        return citizen;
    }
}
