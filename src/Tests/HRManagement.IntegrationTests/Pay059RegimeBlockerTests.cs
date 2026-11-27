using HRManagement.SharedKernel.Legal;
using HRManagement.TestSupport.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Payroll.Application.Abstractions;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-59 (§1, P-6): the service-regime gate's blocked path, on PLAN-34's host as it is (<see cref="PayrollIntegrationTestSetup"/>, through
/// <see cref="Pay056RunScope"/>). An agency on <c>Payroll:Blockers:Mode=BlockRun</c> whose HR.Core blocks an employee has its calculation
/// refused before the run moves, and the refusal names the employee; a host with no HR.Core client refuses (<c>ClientMissing</c>) while
/// <c>ServiceRegimes:BlocksFinalPayroll</c> keeps its default, and calculates when the agency turned it off. The default mode
/// (<c>HoldEmployee</c>: everyone calculated, the blocked employee's order held) is proved by
/// <see cref="Pay058HeldEmployeeTests.A_regime_blocker_holds_the_blocked_employee_by_name_after_a_full_calculation_and_the_others_are_paid"/>,
/// and not repeated here.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Pay059RegimeBlockerTests
{
    private const string BlockedName = "موظف محجوب للاختبار";

    private static async Task AssertLeftAsItWasAsync(Pay056RunScope scope, Guid runId)
    {
        var run = await scope.RunAsync(runId);
        Assert.Equal((PayrollRunStatus.Draft, (Guid?)null, (string?)null), (run.Status, run.PayrollConfigurationId, run.ConfigContentHash));
        Assert.Empty(await scope.Db.PayrollRunJobs.AsNoTracking().Where(j => j.PayrollRunId == runId).ToListAsync());
        Assert.Empty(await scope.LinesAsync(runId));
        Assert.Empty(await scope.Db.PaymentHolds.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task In_BlockRun_an_employee_HR_Core_blocks_refuses_the_calculation_by_his_name_and_the_run_is_left_as_it_was()
    {
        var hrCore = new FakeHrCoreServiceRegimeClient { ReasonCode = "REGIME_UNCLASSIFIED" };
        var lookup = new Mock<IEmployeeLookup>();
        Guid blocked = Guid.Empty;
        lookup.Setup(l => l.GetEmployeeDisplayNamesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => id == blocked ? BlockedName : EmployeeLookupLabels.Unavailable));
        await using var scope = await Pay056RunScope.CreateAsync(services =>
        {
            services.AddSingleton<IHrCoreServiceRegimeClient>(hrCore);
            services.AddSingleton<IEmployeeLookup>(lookup.Object);
            services.Configure<PayrollBlockerOptions>(o => o.Mode = PayrollBlockerMode.BlockRun);
        });
        blocked = await scope.EmployeeAsync();
        await scope.EmployeeAsync();
        hrCore.BlockedEmployeeIds.Add(blocked);
        var runId = await scope.CreateRunAsync();

        var (ok, lines, error) = await scope.CalculateAsync(runId);

        Assert.False(ok);
        Assert.Equal(0, lines);
        Assert.Equal(PayrollRegimeGovernance.BlockRunRefusal(
            [new HrCorePayrollBlockerDto(blocked, true, "REGIME_UNCLASSIFIED")], new Dictionary<Guid, string> { [blocked] = BlockedName }), error);
        Assert.Contains(BlockedName, error, StringComparison.Ordinal);
        Assert.Contains("REGIME_UNCLASSIFIED", error, StringComparison.Ordinal);
        Assert.Contains("1 موظفاً", error, StringComparison.Ordinal);
        // The lookup was asked for the blocked employee only.
        lookup.Verify(l => l.GetEmployeeDisplayNamesAsync(
            It.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { blocked })), It.IsAny<CancellationToken>()), Times.Once);
        await AssertLeftAsItWasAsync(scope, runId);
    }

    [Fact]
    public async Task A_host_with_no_HR_Core_client_refuses_the_calculation_while_BlocksFinalPayroll_keeps_its_default()
    {
        await using var scope = await Pay056RunScope.CreateAsync(services => services.RemoveAll<IHrCoreServiceRegimeClient>());
        await scope.EmployeeAsync();
        var runId = await scope.CreateRunAsync();

        var (ok, lines, error) = await scope.CalculateAsync(runId);

        Assert.False(ok);
        Assert.Equal(0, lines);
        Assert.Equal(PayrollRegimeGovernance.ClientMissing, error);
        await AssertLeftAsItWasAsync(scope, runId);
    }

    [Fact]
    public async Task A_host_with_no_HR_Core_client_calculates_when_the_agency_turned_BlocksFinalPayroll_off()
    {
        await using var scope = await Pay056RunScope.CreateAsync(services =>
        {
            services.RemoveAll<IHrCoreServiceRegimeClient>();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceRegimes:BlocksFinalPayroll"] = "false" })
                .Build());
        });
        var employee = await scope.EmployeeAsync();
        var runId = await scope.CreateRunAsync();

        var (ok, lines, error) = await scope.CalculateAsync(runId);

        Assert.True(ok, error);
        Assert.Equal(1, lines);
        Assert.Equal(employee, Assert.Single(await scope.LinesAsync(runId)).EmployeeId);
        Assert.Empty(await scope.Db.PaymentHolds.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void The_refusal_names_the_first_ten_by_display_name_in_the_order_of_their_ids_then_counts_the_others()
    {
        var blocked = Enumerable.Range(1, 12)
            .Select(i => new HrCorePayrollBlockerDto(Guid.Parse($"59000000-0000-4000-8000-{i:D12}"), true, i % 2 == 0 ? "REGIME_UNCLASSIFIED" : null))
            .Reverse()
            .ToList();
        var names = blocked.Take(11).ToDictionary(b => b.EmployeeId, b => $"موظف {b.EmployeeId.ToString("D")[^2..]}");

        var refusal = PayrollRegimeGovernance.BlockRunRefusal(blocked, names);

        Assert.Contains("12 موظفاً", refusal, StringComparison.Ordinal);
        Assert.Contains("و2 آخرون", refusal, StringComparison.Ordinal);
        // The first ten by id: 01..10; 01 has no name (not in the lookup's answer), so it is shown by its id.
        Assert.Contains(blocked[^1].EmployeeId.ToString("D") + " (دون رمز)", refusal, StringComparison.Ordinal);
        foreach (var i in Enumerable.Range(2, 9))
            Assert.Contains($"موظف {i:D2} ({(i % 2 == 0 ? "REGIME_UNCLASSIFIED" : "دون رمز")})", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("موظف 11", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("موظف 12", refusal, StringComparison.Ordinal);
        Assert.True(refusal.IndexOf("موظف 02", StringComparison.Ordinal) < refusal.IndexOf("موظف 10", StringComparison.Ordinal));
    }
}
