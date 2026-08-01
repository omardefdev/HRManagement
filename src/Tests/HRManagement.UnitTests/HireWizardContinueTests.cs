using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using HR.Contracts.Web.Controllers;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

public class HireWizardContinueTests
{
    private static HireWizardController CreateController(ContractsDbContext db, ContractsRepository repo, IMediator mediator)
    {
        var httpContext = new DefaultHttpContext();
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return new HireWizardController(repo, mediator, db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = tempData
        };
    }

    private static async Task<(ContractsDbContext Db, ContractsRepository Repo)> CreateDbAsync()
    {
        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ContractsDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, new ContractsRepository(db));
    }

    [Fact]
    public async Task Continue_WithOpenAppointment_RedirectsToStep2()
    {
        var (db, repo) = await CreateDbAsync();
        await using (db)
        {
            var employeeId = Guid.NewGuid();
            var txn = new ServiceTransaction
            {
                EmployeeId = employeeId,
                TransactionType = ServiceTransactionType.Appointment,
                Notes = "أحمد علي — مصدر توظيف: DEFAULT"
            };
            txn.MoveTo(ServiceTransactionState.Submitted, null, null);
            txn.MoveTo(ServiceTransactionState.UnderReview, null, null);
            txn.MoveTo(ServiceTransactionState.InProcess, null, null);
            db.ServiceTransactions.Add(txn);
            await db.SaveChangesAsync();

            var mediator = new Mock<IMediator>();
            var controller = CreateController(db, repo, mediator.Object);

            var result = await controller.Continue(employeeId);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(HireWizardController.Step2), redirect.ActionName);
            Assert.Equal(txn.Id, redirect.RouteValues!["transactionId"]);
        }
    }

    [Fact]
    public async Task Continue_WithoutAppointment_RedirectsToIndexWithError()
    {
        var (db, repo) = await CreateDbAsync();
        await using (db)
        {
            var mediator = new Mock<IMediator>();
            var controller = CreateController(db, repo, mediator.Object);

            var result = await controller.Continue(Guid.NewGuid());

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(HireWizardController.Index), redirect.ActionName);
            Assert.NotNull(controller.TempData["Error"]);
        }
    }
}
