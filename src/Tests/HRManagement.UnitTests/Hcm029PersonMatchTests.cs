using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm029TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-029 (A1/A2): person matching at hire — national-id hash first, name + birth date as a warning only.</summary>
public class Hcm029PersonMatchTests
{
    [Fact]
    public async Task National_id_match_is_by_hash_not_by_the_encrypted_column()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var existing = await SeedEmployeeAsync(db, "0129290001");
        var service = new HirePersonMatchService(db);

        var match = await service.FindByNationalIdAsync("  0129290001 ");

        Assert.Equal(existing.Id, match?.Id);
        Assert.StartsWith("ENC:", existing.NationalId);
        Assert.Equal(existing.Id, (await service.FindByNationalIdHashAsync(PiiMask.Sha256Hex("0129290001")))?.Id);
    }

    [Fact]
    public async Task No_match_for_an_unknown_or_blank_national_id()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        await SeedEmployeeAsync(db, "0129290001");
        var service = new HirePersonMatchService(db);

        Assert.Null(await service.FindByNationalIdAsync("0129299999"));
        Assert.Null(await service.FindByNationalIdAsync("   "));
        var match = await service.MatchAsync(null, null, null);
        Assert.Null(match.NationalIdMatch);
        Assert.Null(match.PossibleMatch);
    }

    [Fact]
    public async Task Possible_match_by_name_and_birth_date_is_reported_but_does_not_block_the_hire()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var birthDate = new DateTime(1991, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var lookalike = await SeedEmployeeAsync(db, "0129290002", "سامر خليل", birthDate);

        var match = await new HirePersonMatchService(db).MatchAsync(PiiMask.Sha256Hex(winner.NationalId), "سامر خليل", birthDate.AddHours(9));
        Assert.Null(match.NationalIdMatch);
        Assert.Equal(lookalike.Id, match.PossibleMatch?.Id);

        // Same name and birth date, different national id: the closure creates a new employee.
        var hired = await CreateClosure(db).HireAndCreateEmployeeAsync(
            winner, winner.NationalId!, "سامر", null, "خليل", birthDate, Gender.Male, null,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), nameof(EmploymentType.Permanent));

        Assert.NotEqual(lookalike.Id, hired.Id);
        Assert.Equal(2, await db.Employees.CountAsync());
    }

    [Fact]
    public async Task Possible_match_is_dropped_when_it_is_the_national_id_match()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var birthDate = new DateTime(1988, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var existing = await SeedEmployeeAsync(db, "0129290003", "رنا يوسف", birthDate);

        var match = await new HirePersonMatchService(db).MatchAsync(PiiMask.Sha256Hex("0129290003"), "رنا يوسف", birthDate);

        Assert.Equal(existing.Id, match.NationalIdMatch?.Id);
        Assert.Null(match.PossibleMatch);
    }

    [Fact]
    public async Task Hire_get_preselects_link_existing_when_the_application_national_id_is_on_file()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var existing = await SeedEmployeeAsync(db, winner.NationalId!, "الفائز القائم");
        var controller = CreateHireController(db);

        var result = Assert.IsType<ViewResult>(await controller.Hire(winner.Id));

        var vm = Assert.IsType<RecruitmentHireViewModel>(result.Model);
        Assert.Equal(RecruitmentHireMode.LinkExisting, vm.Mode);
        Assert.Equal(existing.Id, vm.ExistingEmployeeId);
        Assert.Equal("الفائز القائم", vm.MatchedEmployeeName);
        Assert.Contains(vm.Employees!, i => i.Value == existing.Id.ToString() && i.Selected);
    }

    [Fact]
    public async Task Hire_get_stays_on_create_new_without_a_match()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        Assert.Equal(RecruitmentApplicationStatus.OfferAccepted, winner.Status);

        var result = Assert.IsType<ViewResult>(await CreateHireController(db).Hire(winner.Id));

        var vm = Assert.IsType<RecruitmentHireViewModel>(result.Model);
        Assert.Equal(RecruitmentHireMode.CreateNew, vm.Mode);
        Assert.Null(vm.MatchedEmployeeName);
        Assert.True(vm.ApplicationHasNationalId);
    }
}
