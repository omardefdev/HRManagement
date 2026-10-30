using System.Collections;
using System.Security.Claims;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.Entities;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;
using ContractsControllers = HR.Contracts.Web.Controllers;
using PayrollControllers = Payroll.Web.Controllers;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-34 §4 (AC-QA-34d): the twelve <c>Index(employeeId)</c> actions of HR.Contracts and Payroll apply branch scope.
/// A branch user (branch-manager, scope = BR1) sees BR1 employees only, gets 403 for a BR2 employee or for an
/// employee the service cannot place (no snapshot / no active pay profile: fail-closed), and a central user is
/// unchanged. Real controllers, real MediatR handlers and repository, SQLite.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Plan034ScopedIndexTests
{
    public static readonly Guid Tenant = Guid.Parse("34340000-0000-4000-8000-000000000001");
    public static readonly Guid Br1 = Guid.Parse("34340000-0000-4000-8000-0000000000b1");
    public static readonly Guid Br2 = Guid.Parse("34340000-0000-4000-8000-0000000000b2");
    public static readonly Guid EmployeeBr1 = Guid.Parse("34340000-0000-4000-8000-0000000000e1");
    public static readonly Guid EmployeeBr2 = Guid.Parse("34340000-0000-4000-8000-0000000000e2");
    /// <summary>No org snapshot in HR.Contracts and no active pay profile in Payroll.</summary>
    public static readonly Guid EmployeeUnplaced = Guid.Parse("34340000-0000-4000-8000-0000000000e3");

    public static readonly string[] Actions =
    [
        "Contracts/SeparationRecords", "Contracts/ProbationRecords", "Contracts/CommencementRecords",
        "Contracts/ServiceTransactions", "Contracts/Contracts", "Contracts/HireWizard",
        "Payroll/PaymentHolds", "Payroll/EndOfService", "Payroll/AllowanceRecords",
        "Payroll/DeductionRecords", "Payroll/PayrollAdjustments", "Payroll/FraudAlerts"
    ];

    public static TheoryData<string> AllActions()
    {
        var data = new TheoryData<string>();
        foreach (var a in Actions) data.Add(a);
        return data;
    }

    public static ClaimsPrincipal BranchUser(Guid scopeOrgUnit) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, "34340000-0000-4000-8000-0000000000a1"),
        new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager),
        new Claim(ClaimTypes.Role, HrManagementRoles.HrCore),
        new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, scopeOrgUnit.ToString("D")),
        new Claim(TenantContext.TenantClaimType, Tenant.ToString("D"))
    ], "test"));

    public static ClaimsPrincipal BranchUserWithInvalidClaim() => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, "34340000-0000-4000-8000-0000000000a2"),
        new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager),
        new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, "not-a-guid")
    ], "test"));

    public static ClaimsPrincipal CentralUser() => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, "34340000-0000-4000-8000-0000000000c1"),
        new Claim(ClaimTypes.Role, HrManagementRoles.HrCore),
        new Claim(TenantContext.TenantClaimType, Tenant.ToString("D"))
    ], "test"));

    [Theory]
    [MemberData(nameof(AllActions))]
    public async Task Branch_user_sees_own_branch_employee(string action)
    {
        await using var host = await ScopedIndexHost.CreateSqliteAsync();
        var rows = AssertRows(await host.InvokeAsync(action, BranchUser(Br1), EmployeeBr1));
        Assert.Contains(EmployeeBr1, rows);
        Assert.All(rows, id => Assert.Equal(EmployeeBr1, id));
    }

    [Theory]
    [MemberData(nameof(AllActions))]
    public async Task Branch_user_gets_403_for_other_branch_employee(string action)
    {
        await using var host = await ScopedIndexHost.CreateSqliteAsync();
        Assert.IsType<ForbidResult>(await host.InvokeAsync(action, BranchUser(Br1), EmployeeBr2));
    }

    [Theory]
    [MemberData(nameof(AllActions))]
    public async Task Branch_user_gets_403_for_an_employee_the_service_cannot_place(string action)
    {
        await using var host = await ScopedIndexHost.CreateSqliteAsync();
        Assert.IsType<ForbidResult>(await host.InvokeAsync(action, BranchUser(Br1), EmployeeUnplaced));
    }

    [Theory]
    [MemberData(nameof(AllActions))]
    public async Task Unfiltered_list_for_branch_user_holds_own_branch_only(string action)
    {
        await using var host = await ScopedIndexHost.CreateSqliteAsync();
        var rows = AssertRows(await host.InvokeAsync(action, BranchUser(Br1), null));
        Assert.Contains(EmployeeBr1, rows);
        Assert.DoesNotContain(EmployeeBr2, rows);
        Assert.DoesNotContain(EmployeeUnplaced, rows);
    }

    [Theory]
    [MemberData(nameof(AllActions))]
    public async Task Central_user_is_unchanged_and_sees_every_employee(string action)
    {
        await using var host = await ScopedIndexHost.CreateSqliteAsync();
        var rows = AssertRows(await host.InvokeAsync(action, CentralUser(), null));
        Assert.Contains(EmployeeBr1, rows);
        Assert.Contains(EmployeeBr2, rows);
        Assert.Contains(EmployeeUnplaced, rows);

        var filtered = AssertRows(await host.InvokeAsync(action, CentralUser(), EmployeeBr2));
        Assert.Contains(EmployeeBr2, filtered);
    }

    [Theory]
    [MemberData(nameof(AllActions))]
    public async Task Branch_user_with_invalid_scope_claim_gets_403(string action)
    {
        await using var host = await ScopedIndexHost.CreateSqliteAsync();
        Assert.IsType<ForbidResult>(await host.InvokeAsync(action, BranchUserWithInvalidClaim(), null));
    }

    public static IReadOnlyList<Guid> AssertRows(IActionResult result)
    {
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable>(view.Model);
        return model.Cast<object>()
            .Select(o => (Guid)o.GetType().GetProperty("EmployeeId")!.GetValue(o)!)
            .ToList();
    }
}

/// <summary>
/// Seeds HR.Contracts and Payroll with BR1/BR2 org units, a BR1 employee, a BR2 employee and an unplaced employee,
/// one record of each listed kind per employee, and invokes the controllers' Index actions.
/// </summary>
public sealed class ScopedIndexHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly List<IDisposable> _owned;

    private ScopedIndexHost(ServiceProvider provider, List<IDisposable> owned)
    {
        _provider = provider;
        _owned = owned;
    }

    public static async Task<ScopedIndexHost> CreateSqliteAsync()
    {
        var contractsConnection = new SqliteConnection("Data Source=:memory:");
        var payrollConnection = new SqliteConnection("Data Source=:memory:");
        contractsConnection.Open();
        payrollConnection.Open();
        var host = Build(
            o => o.UseSqlite(contractsConnection),
            o => o.UseSqlite(payrollConnection),
            [contractsConnection, payrollConnection]);
        await host.SeedAsync(ensureCreated: true);
        return host;
    }

    public static ScopedIndexHost Build(
        Action<DbContextOptionsBuilder> contracts,
        Action<DbContextOptionsBuilder> payroll,
        List<IDisposable> owned)
    {
        var lookup = new Mock<IHrCoreLookupClient>();
        lookup.Setup(l => l.GetEmployeesByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var employeeLookup = new Mock<IEmployeeLookup>();
        employeeLookup.Setup(l => l.GetEmployeeDisplayNamesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(contracts);
        services.AddDbContext<PayrollDbContext>(payroll);
        services.AddScoped<IContractsRepository, ContractsRepository>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GetSeparationRecordsQuery>());
        services.AddSingleton(lookup.Object);
        services.AddSingleton(employeeLookup.Object);
        return new ScopedIndexHost(services.BuildServiceProvider(), owned);
    }

    public async Task SeedAsync(bool ensureCreated)
    {
        await using var scope = _provider.CreateAsyncScope();
        var contracts = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        var payroll = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        if (ensureCreated)
        {
            await contracts.Database.EnsureCreatedAsync();
            await payroll.Database.EnsureCreatedAsync();
        }

        var t = Plan034ScopedIndexTests.Tenant;
        var br1 = Plan034ScopedIndexTests.Br1;
        var br2 = Plan034ScopedIndexTests.Br2;
        OrgUnitSnapshot Unit(Guid id, string code) => new()
        {
            Id = id,
            TenantId = t,
            MaterializedPath = $"/{id:D}/",
            Code = code,
            NameAr = code
        };
        contracts.OrgUnitSnapshots.AddRange(Unit(br1, "BR1"), Unit(br2, "BR2"));
        payroll.OrgUnitSnapshots.AddRange(Unit(br1, "BR1"), Unit(br2, "BR2"));
        contracts.EmployeeOrgUnitSnapshots.AddRange(
            new EmployeeOrgUnitSnapshot { EmployeeId = Plan034ScopedIndexTests.EmployeeBr1, TenantId = t, PrimaryOrgUnitId = br1 },
            new EmployeeOrgUnitSnapshot { EmployeeId = Plan034ScopedIndexTests.EmployeeBr2, TenantId = t, PrimaryOrgUnitId = br2 });

        var contractType = new ContractType { Code = "P34", NameAr = "عقد" };
        contracts.ContractTypes.Add(contractType);
        var allowanceType = new CompensationType { Code = "P34-ALW", NameAr = "بدل" };
        payroll.CompensationTypes.Add(allowanceType);

        var employees = new (Guid Id, Guid? OrgUnit)[]
        {
            (Plan034ScopedIndexTests.EmployeeBr1, br1),
            (Plan034ScopedIndexTests.EmployeeBr2, br2),
            (Plan034ScopedIndexTests.EmployeeUnplaced, null)
        };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var (id, orgUnit) in employees)
        {
            var contract = new Contract
            {
                EmployeeId = id,
                ContractType = contractType,
                StartDate = today.AddYears(-1),
                Status = ContractStatus.Active
            };
            contracts.Contracts.Add(contract);
            contracts.SeparationRecords.Add(new SeparationRecord
            {
                EmployeeId = id,
                Contract = contract,
                SeparationType = SeparationType.Resignation,
                EffectiveDate = today
            });
            contracts.ProbationRecords.Add(new ProbationRecord
            {
                EmployeeId = id,
                Contract = contract,
                StartDate = today.AddYears(-1),
                EndDate = today.AddMonths(-9),
                Result = ProbationResult.Passed
            });
            contracts.CommencementRecords.Add(new CommencementRecord { EmployeeId = id, Contract = contract, StartDate = today.AddYears(-1) });
            contracts.ServiceTransactions.Add(new ServiceTransaction { TenantId = t, EmployeeId = id, TransactionType = ServiceTransactionType.Appointment });

            if (orgUnit is not null)
            {
                payroll.EmployeePayProfiles.Add(new EmployeePayProfile
                {
                    EmployeeId = id,
                    BaseSalary = 1000m,
                    EffectiveFrom = DateTime.UtcNow.AddYears(-1),
                    IsActive = true,
                    OrgUnitId = orgUnit
                });
            }
            else
            {
                // an inactive profile with an org unit must not place the employee
                payroll.EmployeePayProfiles.Add(new EmployeePayProfile
                {
                    EmployeeId = id,
                    BaseSalary = 1000m,
                    EffectiveFrom = DateTime.UtcNow.AddYears(-2),
                    IsActive = false,
                    OrgUnitId = br1
                });
            }

            payroll.PaymentHolds.Add(new PaymentHold { EmployeeId = id, Reason = "P34" });
            payroll.EndOfServiceSettlements.Add(new EndOfServiceSettlement { EmployeeId = id });
            payroll.AllowanceRecords.Add(new AllowanceRecord { EmployeeId = id, AllowanceType = allowanceType, Amount = 10m, EffectiveFrom = DateTime.UtcNow });
            payroll.DeductionRecords.Add(new DeductionRecord { EmployeeId = id, DeductionType = "P34", Amount = 5m });
            payroll.PayrollAdjustments.Add(new PayrollAdjustment { EmployeeId = id, Amount = 1m, AdjustmentReason = "P34" });
            payroll.FraudAlerts.Add(new FraudAlert { EmployeeId = id, RuleId = "P34", Category = "P34", Severity = "Low" });
        }

        await contracts.SaveChangesAsync();
        await payroll.SaveChangesAsync();
    }

    public async Task<IActionResult> InvokeAsync(string action, ClaimsPrincipal user, Guid? employeeId)
    {
        await using var scope = _provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var http = new DefaultHttpContext { User = user, RequestServices = sp };
        T With<T>(T controller) where T : Controller
        {
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
            return controller;
        }

        var cdb = sp.GetRequiredService<ContractsDbContext>();
        var pdb = sp.GetRequiredService<PayrollDbContext>();
        var mediator = sp.GetRequiredService<IMediator>();
        var repo = sp.GetRequiredService<IContractsRepository>();
        var lookup = sp.GetRequiredService<IHrCoreLookupClient>();
        var employees = sp.GetRequiredService<IEmployeeLookup>();

        return action switch
        {
            "Contracts/SeparationRecords" => await With(new ContractsControllers.SeparationRecordsController(mediator, lookup, cdb)).Index(null, employeeId, null),
            "Contracts/ProbationRecords" => await With(new ContractsControllers.ProbationRecordsController(mediator, lookup, cdb)).Index(null, employeeId),
            "Contracts/CommencementRecords" => await With(new ContractsControllers.CommencementRecordsController(mediator, lookup, cdb)).Index(employeeId, null, null, null),
            "Contracts/ServiceTransactions" => await With(new ContractsControllers.ServiceTransactionsController(repo, lookup, cdb)).Index(null, null, employeeId),
            "Contracts/Contracts" => await With(new ContractsControllers.ContractsController(cdb, mediator, lookup)).Index(null, employeeId),
            "Contracts/HireWizard" => await With(new ContractsControllers.HireWizardController(repo, mediator, cdb, lookup)).Index(null, employeeId),
            "Payroll/PaymentHolds" => await With(new PayrollControllers.PaymentHoldsController(pdb, null!, employees)).Index(employeeId, null, null, null, null),
            "Payroll/EndOfService" => await With(new PayrollControllers.EndOfServiceController(pdb, mediator, employees)).Index(employeeId, null, null, null),
            "Payroll/AllowanceRecords" => await With(new PayrollControllers.AllowanceRecordsController(pdb, employees)).Index(employeeId, null, null, null),
            "Payroll/DeductionRecords" => await With(new PayrollControllers.DeductionRecordsController(pdb, employees)).Index(employeeId, null, null, null, null),
            "Payroll/PayrollAdjustments" => await With(new PayrollControllers.PayrollAdjustmentsController(pdb, employees)).Index(employeeId, null, null, null),
            "Payroll/FraudAlerts" => await With(new PayrollControllers.FraudAlertsController(pdb, null!, employees)).Index(null, null, null, employeeId, null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        foreach (var d in _owned)
            d.Dispose();
    }
}
