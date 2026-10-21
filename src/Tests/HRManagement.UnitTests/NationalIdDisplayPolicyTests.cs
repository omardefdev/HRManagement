using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Enums;

namespace HRManagement.UnitTests;

public class NationalIdDisplayPolicyTests
{
    private const string RawNationalId = AcceptanceSeedIds.NationalIdBr1;

    [Fact]
    public void Branch_manager_sees_masked_even_with_hr_core()
    {
        var user = UserWithRoles(HrManagementRoles.BranchManager, HrManagementRoles.HrCore);
        var employee = EmployeeWithNationalId(RawNationalId);

        Assert.False(EmployeeNationalIdPresentation.CanViewFullNationalId(user));
        Assert.Equal(PiiMask.NationalId(RawNationalId),
            EmployeeNationalIdPresentation.ResolveDisplayNationalId(employee, user));
    }

    [Fact]
    public void Central_hr_core_sees_full_national_id()
    {
        var user = UserWithRoles(HrManagementRoles.HrCore);
        var employee = EmployeeWithNationalId(RawNationalId);

        Assert.True(EmployeeNationalIdPresentation.CanViewFullNationalId(user));
        Assert.Equal(RawNationalId, EmployeeNationalIdPresentation.ResolveDisplayNationalId(employee, user));
    }

    [Fact]
    public void Admin_sees_full_national_id()
    {
        var user = UserWithRoles(HrManagementRoles.Admin);
        var employee = EmployeeWithNationalId(RawNationalId);

        Assert.True(EmployeeNationalIdPresentation.CanViewFullNationalId(user));
        Assert.Equal(RawNationalId, EmployeeNationalIdPresentation.ResolveDisplayNationalId(employee, user));
    }

    private static Employee EmployeeWithNationalId(string nationalId) => new()
    {
        Id = AcceptanceSeedIds.EmpBr1Id,
        NationalId = nationalId,
        FirstName = "test",
        LastName = "user",
        Gender = Gender.Male
    };

    private static ClaimsPrincipal UserWithRoles(params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();
        claims.Add(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
