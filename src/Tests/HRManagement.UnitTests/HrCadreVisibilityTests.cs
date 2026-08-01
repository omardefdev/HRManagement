using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;

namespace HRManagement.UnitTests;

public class HrCadreVisibilityTests
{
    private static ClaimsPrincipal MakeUser(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "testuser") };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    public void Admin_sees_all()
    {
        var user = MakeUser(HrManagementRoles.Admin);
        Assert.Null(HrCadreVisibility.GetRestrictedCadre(user));
    }

    [Fact]
    public void HrCore_sees_all()
    {
        var user = MakeUser(HrManagementRoles.HrCore);
        Assert.Null(HrCadreVisibility.GetRestrictedCadre(user));
    }

    [Fact]
    public void Supervisory_only_returns_Supervisory()
    {
        var user = MakeUser(HrManagementRoles.HrCoreSupervisory);
        Assert.Equal(EmployeeCadre.Supervisory, HrCadreVisibility.GetRestrictedCadre(user));
    }

    [Fact]
    public void Executive_only_returns_Executive()
    {
        var user = MakeUser(HrManagementRoles.HrCoreExecutive);
        Assert.Equal(EmployeeCadre.Executive, HrCadreVisibility.GetRestrictedCadre(user));
    }

    [Fact]
    public void Both_supervisory_and_executive_sees_all()
    {
        var user = MakeUser(HrManagementRoles.HrCoreSupervisory, HrManagementRoles.HrCoreExecutive);
        Assert.Null(HrCadreVisibility.GetRestrictedCadre(user));
    }

    [Fact]
    public void User_without_relevant_roles_sees_all()
    {
        var user = MakeUser(HrManagementRoles.Payroll);
        Assert.Null(HrCadreVisibility.GetRestrictedCadre(user));
    }
}
