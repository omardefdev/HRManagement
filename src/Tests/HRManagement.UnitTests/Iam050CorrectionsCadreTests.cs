using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Controllers;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Legal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-50 (review 9 and its verification): EmployeeCorrections reads under HrCore.ReadOnly since PLAN-50, so its list applies the cadre
/// restriction of the employees list (an employee of the other cadre asked by id is refused), and the two approvals the approver now
/// reaches through the gateway (EmployeeCorrections.Approve, EmploymentRecords.Approve) act only on an employee the caller may read: in his
/// org scope and, when he is cadre-restricted, of his cadre. hr-core sees and approves all, as before.
/// </summary>
public class Iam050CorrectionsCadreTests
{
    private static HRCoreDbContext Db() =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Employee Employee(EmployeeCadre cadre, string nationalId, Guid? orgUnit = null) => new()
    {
        NationalId = nationalId,
        FirstName = "موظف",
        LastName = "اختبار",
        FullName = "موظف اختبار",
        BirthDate = new DateTime(1990, 1, 1),
        Gender = Gender.Male,
        Cadre = cadre,
        PrimaryOrgUnitId = orgUnit
    };

    private static ClaimsPrincipal User(params Claim[] extra) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")), .. extra], "Test", ClaimTypes.Name, ClaimTypes.Role));

    private static Claim Role(string role) => new(ClaimTypes.Role, role);

    private static OrgScopeService Scope(HRCoreDbContext db) =>
        new(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions())));

    private static T With<T>(T controller, ClaimsPrincipal user) where T : Controller
    {
        var http = new DefaultHttpContext { User = user };
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static EmployeeCorrectionsController Corrections(HRCoreDbContext db, ClaimsPrincipal user) =>
        With(new EmployeeCorrectionsController(db, Scope(db)), user);

    private static EmployeeCorrectionsController Corrections(HRCoreDbContext db, string role) => Corrections(db, User(Role(role)));

    private static EmploymentRecordsController Records(HRCoreDbContext db, ClaimsPrincipal user) =>
        With(new EmploymentRecordsController(db, Scope(db), Mock.Of<IServiceRegimeProvider>()), user);

    [Fact]
    public async Task A_reader_of_one_cadre_lists_the_corrections_of_that_cadre_only()
    {
        await using var db = Db();
        var supervisory = Employee(EmployeeCadre.Supervisory, "50900000001");
        var executive = Employee(EmployeeCadre.Executive, "50900000002");
        db.Employees.AddRange(supervisory, executive);
        await db.SaveChangesAsync();
        var ofSupervisory = new EmployeeCorrection { EmployeeId = supervisory.Id, Reason = "a", RequestedBy = Guid.NewGuid() };
        var ofExecutive = new EmployeeCorrection { EmployeeId = executive.Id, Reason = "b", RequestedBy = Guid.NewGuid() };
        db.EmployeeCorrections.AddRange(ofSupervisory, ofExecutive);
        await db.SaveChangesAsync();

        async Task<Guid[]> ListedFor(string role)
        {
            var view = Assert.IsType<ViewResult>(await Corrections(db, role).Index(null, null, null, null, null, CancellationToken.None));
            return Assert.IsType<EmployeeCorrectionsIndexViewModel>(view.Model).Items.Select(c => c.Id).Order().ToArray();
        }

        Assert.Equal([ofSupervisory.Id], await ListedFor(HrManagementRoles.HrCoreSupervisory));
        Assert.Equal([ofExecutive.Id], await ListedFor(HrManagementRoles.HrCoreExecutive));
        Assert.Equal(new[] { ofSupervisory.Id, ofExecutive.Id }.Order().ToArray(), await ListedFor(HrManagementRoles.HrCore));

        // Asked by employee: the other cadre is refused (as on the employee's page), the own cadre and hr-core are answered.
        Assert.IsType<ForbidResult>(await Corrections(db, HrManagementRoles.HrCoreSupervisory).Index(executive.Id, null, null, null, null, CancellationToken.None));
        Assert.IsType<ViewResult>(await Corrections(db, HrManagementRoles.HrCoreSupervisory).Index(supervisory.Id, null, null, null, null, CancellationToken.None));
        Assert.IsType<ViewResult>(await Corrections(db, HrManagementRoles.HrCore).Index(executive.Id, null, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_cadre_restricted_approver_does_not_approve_a_correction_or_a_record_of_the_other_cadre()
    {
        await using var db = Db();
        var executive = Employee(EmployeeCadre.Executive, "50900000003");
        db.Employees.Add(executive);
        await db.SaveChangesAsync();
        var correction = new EmployeeCorrection { EmployeeId = executive.Id, Reason = "c", RequestedBy = Guid.NewGuid() };
        var record = new EmploymentRecord { EmployeeId = executive.Id, PositionTitle = "IAM50", StartDate = DateTime.UtcNow.Date };
        db.EmployeeCorrections.Add(correction);
        db.EmploymentRecords.Add(record);
        await db.SaveChangesAsync();
        var restricted = User(Role(HrManagementRoles.HrManagementApprover), Role(HrManagementRoles.HrCoreSupervisory));

        Assert.IsType<ForbidResult>(await Corrections(db, restricted).Approve(correction.Id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await Records(db, restricted).Approve(record.Id));
        Assert.Equal(ApprovalStatus.Pending, (await db.EmployeeCorrections.SingleAsync(c => c.Id == correction.Id)).Status);
        Assert.Null((await db.EmploymentRecords.SingleAsync(r => r.Id == record.Id)).ApprovedBy);

        // The approver with no cadre restriction approves the record (in his own name, as before).
        Assert.IsType<RedirectToActionResult>(await Records(db, User(Role(HrManagementRoles.HrManagementApprover))).Approve(record.Id));
        Assert.NotNull((await db.EmploymentRecords.SingleAsync(r => r.Id == record.Id)).ApprovedBy);
    }

    [Fact]
    public async Task A_branch_scoped_approver_does_not_approve_outside_his_branch()
    {
        await using var db = Db();
        var tenant = Guid.NewGuid();
        var branch = new OrgUnit { TenantId = tenant, Code = "B1", NameAr = "B1" };
        branch.MaterializedPath = $"/{branch.Id:D}/";
        var elsewhere = new OrgUnit { TenantId = tenant, Code = "B2", NameAr = "B2" };
        elsewhere.MaterializedPath = $"/{elsewhere.Id:D}/";
        db.OrgUnits.AddRange(branch, elsewhere);
        var outside = Employee(EmployeeCadre.Executive, "50900000004", elsewhere.Id);
        var inside = Employee(EmployeeCadre.Executive, "50900000005", branch.Id);
        db.Employees.AddRange(outside, inside);
        await db.SaveChangesAsync();
        var outsideRecord = new EmploymentRecord { EmployeeId = outside.Id, PositionTitle = "IAM50", StartDate = DateTime.UtcNow.Date };
        var insideRecord = new EmploymentRecord { EmployeeId = inside.Id, PositionTitle = "IAM50", StartDate = DateTime.UtcNow.Date };
        var outsideCorrection = new EmployeeCorrection { EmployeeId = outside.Id, Reason = "d", RequestedBy = Guid.NewGuid() };
        db.EmploymentRecords.AddRange(outsideRecord, insideRecord);
        db.EmployeeCorrections.Add(outsideCorrection);
        await db.SaveChangesAsync();
        var scoped = User(Role(HrManagementRoles.HrManagementApprover), Role(HrManagementRoles.BranchManager),
            new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, branch.Id.ToString("D")));

        Assert.IsType<ForbidResult>(await Records(db, scoped).Approve(outsideRecord.Id));
        Assert.IsType<ForbidResult>(await Corrections(db, scoped).Approve(outsideCorrection.Id, CancellationToken.None));
        Assert.Null((await db.EmploymentRecords.SingleAsync(r => r.Id == outsideRecord.Id)).ApprovedBy);
        Assert.Equal(ApprovalStatus.Pending, (await db.EmployeeCorrections.SingleAsync(c => c.Id == outsideCorrection.Id)).Status);

        Assert.IsType<RedirectToActionResult>(await Records(db, scoped).Approve(insideRecord.Id));
        Assert.NotNull((await db.EmploymentRecords.SingleAsync(r => r.Id == insideRecord.Id)).ApprovedBy);
    }
}
