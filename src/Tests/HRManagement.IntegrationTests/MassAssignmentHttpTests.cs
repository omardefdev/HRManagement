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

    /// <summary>
    /// PLAN-56 (G-PAY-06): the manual salary line is removed, so the question «which fields of a salary line may a request set» has
    /// one answer: none. No action of Payroll binds a salary line or one of its components; a manual change is an approved adjustment.
    /// </summary>
    [Fact]
    public void No_action_of_Payroll_binds_a_salary_line()
    {
        var actions = typeof(PayrollRunsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .ToList();
        Assert.NotEmpty(actions);

        var binders = actions
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(SalaryLine) || p.ParameterType == typeof(SalaryLineComponent)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();
        Assert.Empty(binders);
        Assert.DoesNotContain(actions, m => m.Name == "AddSalaryLine");
    }
}
