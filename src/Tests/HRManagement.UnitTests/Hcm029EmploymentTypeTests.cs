using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.ViewModels;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm029TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-029 (B): the hire employment category is a domain EmploymentType.</summary>
public class Hcm029EmploymentTypeTests
{
    private static DateOnly Start => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    [Fact]
    public void Form_categories_are_the_employment_type_enum()
    {
        Assert.Equal(Enum.GetNames<EmploymentType>(), RecruitmentHireViewModel.EmploymentCategories.Select(c => c.Code));
        Assert.Equal(["دائم", "إداري", "متعاقد مؤقت"], RecruitmentHireViewModel.EmploymentCategories.Select(c => c.Label));
        Assert.Equal(nameof(EmploymentType.Permanent), new RecruitmentHireViewModel().EmploymentCategory);
        Assert.Equal(EmploymentType.Permanent, new RecruitmentOptions().DefaultEmploymentType);
    }

    [Theory]
    [InlineData("PERMANENT", EmploymentType.Permanent)]
    [InlineData("TEMPORARY", EmploymentType.Temporary)]
    [InlineData("CONTRACT", EmploymentType.Temporary)]
    [InlineData("contract", EmploymentType.Temporary)]
    [InlineData("Permanent", EmploymentType.Permanent)]
    [InlineData(" administrative ", EmploymentType.Administrative)]
    [InlineData("Temporary", EmploymentType.Temporary)]
    public void Legacy_codes_and_enum_names_resolve(string code, EmploymentType expected)
    {
        Assert.True(HireEmploymentTypeRules.TryResolve(code, out var type, out var error));
        Assert.Equal(expected, type);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("INTERNSHIP", HireEmploymentTypeRules.InternshipNotSupportedMessage)]
    [InlineData("internship", HireEmploymentTypeRules.InternshipNotSupportedMessage)]
    [InlineData("1", HireEmploymentTypeRules.InvalidMessage)]
    [InlineData("FREELANCE", HireEmploymentTypeRules.InvalidMessage)]
    [InlineData("", HireEmploymentTypeRules.InvalidMessage)]
    [InlineData(null, HireEmploymentTypeRules.InvalidMessage)]
    public void Internship_and_unknown_codes_are_refused(string? code, string message)
    {
        Assert.False(HireEmploymentTypeRules.TryResolve(code, out _, out var error));
        Assert.Equal(message, error);
    }

    [Fact]
    public async Task Internship_is_refused_by_the_form_and_by_the_closure()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var controller = CreateHireController(db);

        var result = Assert.IsType<ViewResult>(await controller.Hire(Form(winner, "INTERNSHIP")));
        Assert.Contains(
            controller.ModelState[nameof(RecruitmentHireViewModel.EmploymentCategory)]!.Errors,
            e => e.ErrorMessage == HireEmploymentTypeRules.InternshipNotSupportedMessage);
        Assert.NotNull(result.Model);

        var ex = await Assert.ThrowsAsync<HireClosureException>(() => HireAsync(db, winner, "INTERNSHIP"));
        Assert.Equal(HireEmploymentTypeRules.InternshipNotSupportedMessage, ex.Message);
        Assert.Equal(0, await db.Employees.CountAsync());
    }

    [Fact]
    public async Task Non_default_type_without_a_reason_is_refused_by_the_form_and_by_the_closure()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var controller = CreateHireController(db);

        Assert.IsType<ViewResult>(await controller.Hire(Form(winner, nameof(EmploymentType.Administrative), reason: "قصير")));
        Assert.Contains(
            controller.ModelState[nameof(RecruitmentHireViewModel.EmploymentTypeChangeReason)]!.Errors,
            e => e.ErrorMessage == HireEmploymentTypeRules.ChangeReasonRequiredMessage);

        var ex = await Assert.ThrowsAsync<HireClosureException>(() => HireAsync(db, winner, nameof(EmploymentType.Administrative)));
        Assert.Equal(HireClosureErrorKind.InvalidState, ex.Kind);
        Assert.Equal(HireEmploymentTypeRules.ChangeReasonRequiredMessage, ex.Message);
    }

    [Fact]
    public async Task Chosen_type_flows_to_the_employment_record_and_the_employee_created_event()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var publish = CreatePublishMock();

        var employee = await CreateClosure(db, publish.Object).HireAndCreateEmployeeAsync(
            winner, winner.NationalId!, "مرشح", null, "إداري", DateTime.UtcNow.AddYears(-30), Gender.Male, null, Start,
            nameof(EmploymentType.Administrative),
            new HireClosureJustification(ActorUserId: HrUserId, EmploymentTypeChangeReason: ValidReason));

        var record = await db.EmploymentRecords.AsNoTracking().SingleAsync(r => r.EmployeeId == employee.Id);
        Assert.Equal(EmploymentType.Administrative, record.EmploymentType);

        var created = Assert.Single(Published(publish), p => p.Type == typeof(IEmployeeCreated));
        Assert.Equal(nameof(EmploymentType.Administrative), Prop(created.Values, nameof(IEmployeeCreated.EmploymentType)));
        var accepted = Assert.Single(Published(publish), p => p.Type == typeof(ICandidateAccepted));
        Assert.Equal(nameof(EmploymentType.Administrative), Prop(accepted.Values, nameof(ICandidateAccepted.EmploymentCategory)));

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == HireEmploymentTypeRules.ChangeAuditAction);
        Assert.Equal(winner.Id, audit.EntityId);
        Assert.Equal(HrUserId, audit.UserId);
        Assert.Contains("Administrative", audit.ChangedFields);
        Assert.Contains("Permanent", audit.ChangedFields);
    }

    [Fact]
    public async Task Legacy_code_is_mapped_without_a_reason_when_it_matches_the_default()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);

        var employee = await HireAsync(db, winner, "PERMANENT");

        Assert.Equal(EmploymentType.Permanent,
            (await db.EmploymentRecords.AsNoTracking().SingleAsync(r => r.EmployeeId == employee.Id)).EmploymentType);
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == HireEmploymentTypeRules.ChangeAuditAction));
    }

    [Fact]
    public async Task Configured_default_is_preselected_and_needs_no_reason()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, _) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var config = Config((RecruitmentOptions.DefaultEmploymentTypeKey, nameof(EmploymentType.Temporary)));

        var get = Assert.IsType<ViewResult>(await CreateHireController(db, config).Hire(winner.Id));
        var vm = Assert.IsType<RecruitmentHireViewModel>(get.Model);
        Assert.Equal(nameof(EmploymentType.Temporary), vm.EmploymentCategory);
        Assert.Equal(nameof(EmploymentType.Temporary), vm.DefaultEmploymentCategory);

        var employee = await HireAsync(db, winner, "CONTRACT", config: config);
        Assert.Equal(EmploymentType.Temporary,
            (await db.EmploymentRecords.AsNoTracking().SingleAsync(r => r.EmployeeId == employee.Id)).EmploymentType);
    }

    private static RecruitmentHireViewModel Form(HR.Core.Domain.Entities.RecruitmentApplication app, string category, string? reason = null) => new()
    {
        ApplicationId = app.Id,
        Mode = RecruitmentHireMode.CreateNew,
        NationalId = app.NationalId!,
        FirstName = "مرشح",
        LastName = "جديد",
        BirthDate = new DateTime(1993, 7, 1),
        ExpectedStartDate = Start,
        EmploymentCategory = category,
        EmploymentTypeChangeReason = reason
    };

    private static Task<HR.Core.Domain.Entities.Employee> HireAsync(
        HR.Core.Infrastructure.Data.HRCoreDbContext db,
        HR.Core.Domain.Entities.RecruitmentApplication app,
        string category,
        string? reason = null,
        Microsoft.Extensions.Configuration.IConfiguration? config = null) =>
        CreateClosure(db, config: config).HireAndCreateEmployeeAsync(
            app, app.NationalId!, "مرشح", null, "جديد", DateTime.UtcNow.AddYears(-30), Gender.Male, null, Start, category,
            new HireClosureJustification(EmploymentTypeChangeReason: reason));
}
