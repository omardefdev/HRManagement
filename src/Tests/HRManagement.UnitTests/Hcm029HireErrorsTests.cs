using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static HRManagement.UnitTests.Hcm029TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-029 (A3/A4): no 500 on the hire form — duplicates and national-id mismatches become form errors.</summary>
public class Hcm029HireErrorsTests
{
    private static DateOnly Start => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    private static RecruitmentHireViewModel CreateNewForm(RecruitmentApplication app, string nationalId, string? reason = null) => new()
    {
        ApplicationId = app.Id,
        Mode = RecruitmentHireMode.CreateNew,
        NationalId = nationalId,
        FirstName = "مرشح",
        LastName = "جديد",
        BirthDate = new DateTime(1993, 7, 1),
        Gender = Gender.Female,
        ExpectedStartDate = Start,
        EmploymentCategory = nameof(EmploymentType.Permanent),
        NationalIdMismatchReason = reason
    };

    [Fact]
    public async Task Create_new_with_an_existing_national_id_is_a_form_error_not_an_exception()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var existing = await SeedEmployeeAsync(db, winner.NationalId!, "موظف قائم");
        var controller = CreateHireController(db);

        var result = Assert.IsType<ViewResult>(await controller.Hire(CreateNewForm(winner, winner.NationalId!)));

        Assert.Equal(nameof(controller.Hire), result.ViewName);
        var errors = controller.ModelState[nameof(RecruitmentHireViewModel.NationalId)]!.Errors;
        Assert.Contains(errors, e => e.ErrorMessage == HireClosureException.DuplicatePersonMessage);
        var vm = Assert.IsType<RecruitmentHireViewModel>(result.Model);
        Assert.Equal(existing.Id, vm.MatchedEmployeeId);
        Assert.Equal(existing.Id, vm.ExistingEmployeeId);
        Assert.Equal(1, await db.Employees.CountAsync());
        Assert.Equal(RecruitmentApplicationStatus.OfferAccepted,
            (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == winner.Id)).Status);
    }

    [Fact]
    public async Task Closure_refuses_a_duplicate_person_with_a_hire_closure_exception_and_leaves_nothing_tracked()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        await SeedEmployeeAsync(db, winner.NationalId!);
        var publish = CreatePublishMock();

        var ex = await Assert.ThrowsAsync<HireClosureException>(() => CreateClosure(db, publish.Object).HireAndCreateEmployeeAsync(
            winner, winner.NationalId!, "مرشح", null, "جديد", DateTime.UtcNow.AddYears(-30), Gender.Male, null, Start,
            nameof(EmploymentType.Permanent)));

        Assert.Equal(HireClosureErrorKind.DuplicatePerson, ex.Kind);
        Assert.Equal(HireClosureException.DuplicatePersonMessage, ex.Message);
        Assert.Empty(Published(publish));
        Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.State != EntityState.Unchanged);
        Assert.Equal(1, await db.Employees.CountAsync());
    }

    [Fact]
    public async Task National_id_mismatch_without_a_reason_is_refused_in_the_form_and_in_the_closure()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var controller = CreateHireController(db);

        Assert.IsType<ViewResult>(await controller.Hire(CreateNewForm(winner, "0129291111", reason: "قصير")));
        Assert.Contains(
            controller.ModelState[nameof(RecruitmentHireViewModel.NationalIdMismatchReason)]!.Errors,
            e => e.ErrorMessage == HireNationalIdRules.MismatchReasonRequiredMessage);

        var ex = await Assert.ThrowsAsync<HireClosureException>(() => CreateClosure(db).HireAndCreateEmployeeAsync(
            winner, "0129291111", "مرشح", null, "جديد", DateTime.UtcNow.AddYears(-30), Gender.Male, null, Start,
            nameof(EmploymentType.Permanent)));
        Assert.Equal(HireClosureErrorKind.InvalidState, ex.Kind);
        Assert.Equal(0, await db.Employees.CountAsync());
        Assert.False(await db.AuditLogs.AnyAsync());
    }

    [Fact]
    public async Task Link_existing_with_a_different_national_id_needs_a_reason()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var other = await SeedEmployeeAsync(db, "0129292222", "موظف آخر");
        var controller = CreateHireController(db);

        var form = new RecruitmentHireViewModel
        {
            ApplicationId = winner.Id,
            Mode = RecruitmentHireMode.LinkExisting,
            ExistingEmployeeId = other.Id,
            ExpectedStartDate = Start,
            EmploymentCategory = nameof(EmploymentType.Permanent)
        };
        var result = Assert.IsType<ViewResult>(await controller.Hire(form));

        Assert.Contains(
            controller.ModelState[nameof(RecruitmentHireViewModel.NationalIdMismatchReason)]!.Errors,
            e => e.ErrorMessage == HireNationalIdRules.MismatchReasonRequiredMessage);
        Assert.Equal(RecruitmentHireMode.LinkExisting, Assert.IsType<RecruitmentHireViewModel>(result.Model).Mode);
    }

    [Fact]
    public async Task Justified_mismatch_is_audited_without_any_national_id_in_the_payload()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        const string entered = "0129293333";

        var employee = await CreateClosure(db).HireAndCreateEmployeeAsync(
            winner, entered, "مرشح", null, "جديد", DateTime.UtcNow.AddYears(-30), Gender.Male, null, Start,
            nameof(EmploymentType.Permanent),
            new HireClosureJustification(NationalIdMismatchReason: ValidReason, ActorUserId: HrUserId));

        Assert.NotEqual(Guid.Empty, employee.Id);
        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == HireNationalIdRules.MismatchAuditAction);
        Assert.Equal(nameof(RecruitmentApplication), audit.EntityType);
        Assert.Equal(winner.Id, audit.EntityId);
        Assert.Equal(HrUserId, audit.UserId);
        var payload = System.Text.Json.JsonDocument.Parse(audit.ChangedFields!).RootElement;
        Assert.Equal(ValidReason, payload.GetProperty("reason").GetString());
        Assert.True(payload.GetProperty("nationalIdMismatch").GetBoolean());
        foreach (var secret in new[] { entered, winner.NationalId!, PiiMask.Sha256Hex(entered), PiiMask.Sha256Hex(winner.NationalId) })
            Assert.DoesNotContain(secret, audit.ChangedFields!);
    }

    [Fact]
    public async Task Matching_national_id_needs_no_reason_and_writes_no_mismatch_audit()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);

        await CreateClosure(db).HireAndCreateEmployeeAsync(
            winner, $" {winner.NationalId} ", "مرشح", null, "جديد", DateTime.UtcNow.AddYears(-30), Gender.Male, null, Start,
            nameof(EmploymentType.Permanent));

        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == HireNationalIdRules.MismatchAuditAction));
    }

    [Fact]
    public void Unique_violation_on_the_national_id_index_maps_to_duplicate_person()
    {
        var pg = new PostgresException(
            "duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            constraintName: "IX_Employees_TenantId_NationalIdHash");

        var mapped = HireClosureException.FromSaveFailure(new DbUpdateException("save failed", pg));

        Assert.Equal(HireClosureErrorKind.DuplicatePerson, mapped?.Kind);
        Assert.Equal(HireClosureException.DuplicatePersonMessage, mapped?.Message);
    }

    [Fact]
    public void Other_unique_violations_and_concurrency_conflicts_map_to_concurrency()
    {
        var otherIndex = new PostgresException(
            "duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            constraintName: "IX_FundedPositionVersions_FundedPositionId_Version");

        Assert.Equal(HireClosureErrorKind.Concurrency,
            HireClosureException.FromSaveFailure(new DbUpdateException("save failed", otherIndex))?.Kind);
        Assert.Equal(HireClosureErrorKind.Concurrency,
            HireClosureException.FromSaveFailure(new DbUpdateConcurrencyException("row changed"))?.Kind);
    }

    [Fact]
    public void Unrelated_save_failures_are_not_mapped()
    {
        var fk = new PostgresException("fk", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation);

        Assert.Null(HireClosureException.FromSaveFailure(new DbUpdateException("save failed", fk)));
        Assert.Null(HireClosureException.FromSaveFailure(new DbUpdateException("save failed")));
    }

    [Fact]
    public async Task Position_no_longer_vacant_is_a_hire_closure_exception()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, req) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var position = await db.FundedPositions.SingleAsync(p => p.Id == req.FundedPositionId);
        position.Status = FundedPositionStatus.Occupied;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<HireClosureException>(() => CreateClosure(db).HireAndCreateEmployeeAsync(
            winner, winner.NationalId!, "مرشح", null, "جديد", DateTime.UtcNow.AddYears(-30), Gender.Male, null, Start,
            nameof(EmploymentType.Permanent)));

        Assert.Equal(HireClosureErrorKind.PositionNotVacant, ex.Kind);
        Assert.Equal(0, await db.Employees.CountAsync());
    }
}
