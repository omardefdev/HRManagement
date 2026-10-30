using HR.Contracts.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Infrastructure.Data;
using static HRManagement.IntegrationTests.Plan034ScopedIndexTests;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-34 §4 (AC-QA-34d, PostgreSQL): the scoped list filter is a <c>Contains</c> over a sub-query on the snapshot /
/// pay-profile tables. These cases prove Npgsql translates it on the migrated schemas, for one HR.Contracts and one
/// Payroll action.
/// </summary>
[Collection("PostgreSql")]
public sealed class Plan034ScopedIndexPostgreSqlTests(PostgreSqlFixture fixture)
{
    private async Task<ScopedIndexHost> CreateAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var contractsCs = await fixture.CreateDatabaseAsync($"p34_contracts_{suffix}");
        var payrollCs = await fixture.CreateDatabaseAsync($"p34_payroll_{suffix}");

        await using (var c = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseNpgsql(contractsCs).Options))
            await c.Database.MigrateAsync();
        await using (var p = new PayrollDbContext(new DbContextOptionsBuilder<PayrollDbContext>().UseNpgsql(payrollCs).Options))
            await p.Database.MigrateAsync();

        var host = ScopedIndexHost.Build(o => o.UseNpgsql(contractsCs), o => o.UseNpgsql(payrollCs), []);
        await host.SeedAsync(ensureCreated: false);
        return host;
    }

    [Trait("Category", "Testcontainers")]
    [SkippableTheory]
    [InlineData("Contracts/Contracts")]
    [InlineData("Payroll/PaymentHolds")]
    public async Task Branch_scope_on_postgresql(string action)
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");
        await using var host = await CreateAsync();

        var own = AssertRows(await host.InvokeAsync(action, BranchUser(Br1), EmployeeBr1));
        Assert.Contains(EmployeeBr1, own);

        Assert.IsType<ForbidResult>(await host.InvokeAsync(action, BranchUser(Br1), EmployeeBr2));
        Assert.IsType<ForbidResult>(await host.InvokeAsync(action, BranchUser(Br1), EmployeeUnplaced));

        var branchList = AssertRows(await host.InvokeAsync(action, BranchUser(Br1), null));
        Assert.Contains(EmployeeBr1, branchList);
        Assert.DoesNotContain(EmployeeBr2, branchList);
        Assert.DoesNotContain(EmployeeUnplaced, branchList);

        var centralList = AssertRows(await host.InvokeAsync(action, CentralUser(), null));
        Assert.Contains(EmployeeBr1, centralList);
        Assert.Contains(EmployeeBr2, centralList);
        Assert.Contains(EmployeeUnplaced, centralList);
    }
}
