using System.Reflection;
using Admin.Logistics.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Payroll.Domain.Entities;
using Payroll.Web.Controllers;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class MassAssignmentHttpTests
{
    [Fact]
    public void InventoryItemFormViewModel_excludes_server_owned_fields()
    {
        Assert.Null(typeof(InventoryItemFormViewModel).GetProperty("TenantId"));
        Assert.Null(typeof(InventoryItemFormViewModel).GetProperty("ApprovedBy"));
        Assert.Null(typeof(InventoryItemFormViewModel).GetProperty("CreatedAt"));
    }

    [Fact]
    public void PayrollRuns_AddSalaryLine_BindAllowlist_excludes_server_owned_fields()
    {
        var param = typeof(PayrollRunsController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(PayrollRunsController.AddSalaryLine))
            .SelectMany(m => m.GetParameters())
            .Single(p => p.ParameterType == typeof(SalaryLine));

        var bind = param.GetCustomAttributes(typeof(BindAttribute), inherit: false).Cast<BindAttribute>().Single();
        Assert.Contains("GrossPay", bind.Include);
        Assert.Contains("EmployeeId", bind.Include);
        Assert.DoesNotContain("NetPay", bind.Include);
        Assert.DoesNotContain("TenantId", bind.Include);
        Assert.DoesNotContain("Id", bind.Include);
    }
}
