using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HRManagement.SharedKernel.Enums;

namespace HRManagement.UnitTests;

public class EmployeeLifecycleServiceTests
{
    [Fact]
    public void TryChangeStatus_Rejects_Invalid_Transition()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            CurrentStatus = EmploymentStatus.Terminated
        };
        var history = new List<EmploymentStatusHistory>();

        var (ok, error) = EmployeeLifecycleService.TryChangeStatus(
            employee, EmploymentStatus.Active, "test", null, history);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Empty(history);
    }

    [Fact]
    public void TryChangeStatus_Applies_OnLeave()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            CurrentStatus = EmploymentStatus.Active
        };
        var history = new List<EmploymentStatusHistory>();

        var (ok, _) = EmployeeLifecycleService.TryChangeStatus(
            employee, EmploymentStatus.OnLeave, "leave", null, history);

        Assert.True(ok);
        Assert.Equal(EmploymentStatus.OnLeave, employee.CurrentStatus);
        Assert.Single(history);
    }
}
