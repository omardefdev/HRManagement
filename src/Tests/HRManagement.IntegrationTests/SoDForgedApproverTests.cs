using System.Reflection;
using System.Security.Claims;
using HR.Contracts.Web.Controllers;
using HR.Core.Web.Controllers;
using HR.Movements.Domain.Entities;
using HR.Movements.Infrastructure.Data;
using HR.Movements.Web.Controllers;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// Penetration-style checks that forged client approver identifiers are ignored/removed (IAM-003).
/// </summary>
[Collection("IntegrationSerial")]
public class SoDForgedApproverTests
{
    [Theory]
    [InlineData(typeof(LeaveRequestsController), "Approve")]
    [InlineData(typeof(LeaveRequestsController), "Reject")]
    [InlineData(typeof(AdvanceRequestsController), "Approve")]
    [InlineData(typeof(AdvanceRequestsController), "Reject")]
    [InlineData(typeof(TransferRequestsController), "Approve")]
    [InlineData(typeof(TransferRequestsController), "Reject")]
    [InlineData(typeof(EmploymentRecordsController), "Approve")]
    public void ApproveActions_DoNotAcceptClientApproverId(Type controllerType, string actionName)
    {
        var methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == actionName)
            .ToList();
        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                p => string.Equals(p.Name, "approverId", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task TransferApprove_IgnoresForgedFormField_UsesTokenOnly()
    {
        var options = new DbContextOptionsBuilder<MovementsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new MovementsDbContext(options);

        var tokenUser = Guid.NewGuid();
        var forgedUser = Guid.NewGuid();
        var creator = Guid.NewGuid();
        var transfer = new TransferRequest
        {
            EmployeeId = Guid.NewGuid(),
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = Guid.NewGuid(),
            RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = ApprovalStatus.Pending,
            RequestedBy = creator
        };
        db.TransferRequests.Add(transfer);
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, tokenUser.ToString("D")),
                new Claim(ClaimTypes.Role, "hr-management-approver")
            ], "TestAuth"))
        };
        // Simulate forged form field still posted by an attacker.
        http.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["approverId"] = forgedUser.ToString("D")
        });

        var controller = new TransferRequestsController(db, Mock.Of<IPublishEndpoint>(), new HRManagement.TestSupport.Integration.FakeHrCoreActiveRegimeClient())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
        };

        await controller.Approve(transfer.Id);

        var updated = await db.TransferRequests.AsNoTracking().SingleAsync(t => t.Id == transfer.Id);
        Assert.Equal(ApprovalStatus.Approved, updated.Status);
        Assert.Equal(tokenUser, updated.ApprovedBy);
        Assert.NotEqual(forgedUser, updated.ApprovedBy);
    }
}
