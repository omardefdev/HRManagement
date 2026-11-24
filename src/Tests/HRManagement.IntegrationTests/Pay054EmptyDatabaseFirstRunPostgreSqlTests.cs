using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.TestSupport.Integration;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-54 (plan Acceptance, P-12): a production agency's first payroll calculation. Payroll.Web starts on an empty PostgreSQL
/// database with <c>Seed:Profile=None</c>: no framework, no bracket, no deduction type, no holiday, no employee. The employees and
/// their pay profiles arrive as they do in production, through Payroll's own consumers of HR.Core's and Contracts' events. A
/// calculation is refused until a framework governs the month, and the refusal says where a framework comes from. A processor then
/// imports the legal pack, a second person approves it, a third attests it, the approver publishes it, and the same run is
/// calculated: pinned to the version, with the version's statutory deductions and nothing from a general table. No row is written by
/// the test itself, and a refused pack leaves none. The clock is the test's (23 November 2026), so the interim gate does not depend
/// on the day the suite runs.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay054EmptyDatabaseFirstRunPostgreSqlTests(PostgreSqlFixture postgres)
{
    private static readonly Guid AttestorUser = Guid.Parse("54000000-0000-4000-8000-0000000000e4");
    private static readonly DateTime October = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OctoberEnd = new(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

    private sealed record ContractActivated(
        Guid ContractId,
        Guid EmployeeId,
        string ContractType,
        DateTime StartDate,
        DateTime? EndDate,
        decimal? BaseSalary,
        string? PayGradeCode = null,
        string? CorrelationId = null,
        string? SourceSystem = null) : IContractActivated;

    private sealed record EmployeeCreated(
        Guid EmployeeId,
        string FullName,
        string NationalIdHash,
        string EmploymentType,
        Guid? TenantId,
        Guid? PrimaryOrgUnitId,
        string? CorrelationId) : IEmployeeCreated
    {
        public string? PayrollRegimeCode => Payroll.Domain.Constants.LegalRegimeCodes.Law50_2004;
    }

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.MessageId).Returns(NewId.NextGuid());
        return context.Object;
    }

    /// <summary>
    /// An employee as Payroll learns of one in production: HR.Core's <c>IEmployeeCreated</c>, then Contracts'
    /// <c>IContractActivated</c> with the agreed salary, each handed to Payroll's own consumer under the agency's tenant.
    /// </summary>
    private static async Task HireAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Guid employee, decimal salary, DateTime start)
    {
        await using var scope = host.Host.Services.CreateAsyncScope();
        TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        try
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await new EmployeeCreatedConsumer(mediator).Consume(Context<IEmployeeCreated>(new EmployeeCreated(
                employee, "موظف الاختبار", new string('a', 64), "Permanent", TenantDefaults.DefaultTenantId, null, null)));
            await new ContractActivatedConsumer(mediator).Consume(Context<IContractActivated>(new ContractActivated(
                Guid.NewGuid(), employee, "Permanent", start, null, salary)));
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    /// <summary>Every table a framework or a calculation reads, counted: an empty database has none of these rows.</summary>
    private static Task<Dictionary<string, int>> RowsAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host) =>
        host.WithDbAsync(async db => new Dictionary<string, int>
        {
            ["PayrollConfigurations"] = await db.Set<PayrollConfiguration>().IgnoreQueryFilters().CountAsync(),
            ["TaxBrackets"] = await db.Set<TaxBracket>().IgnoreQueryFilters().CountAsync(),
            ["StatutoryDeductionRules"] = await db.Set<StatutoryDeductionRule>().IgnoreQueryFilters().CountAsync(),
            ["OfficialHolidays"] = await db.Set<OfficialHoliday>().IgnoreQueryFilters().CountAsync(),
            ["DisbursementPriorityRules"] = await db.Set<DisbursementPriorityRule>().IgnoreQueryFilters().CountAsync(),
            ["PayrollConfigurationFieldSources"] = await db.Set<PayrollConfigurationFieldSource>().IgnoreQueryFilters().CountAsync(),
            ["PayrollConfigurationEditors"] = await db.Set<PayrollConfigurationEditor>().IgnoreQueryFilters().CountAsync(),
            ["LegalDecisionRecords"] = await db.Set<LegalDecisionRecord>().IgnoreQueryFilters().CountAsync(),
            ["LegalAttestations"] = await db.Set<LegalAttestation>().IgnoreQueryFilters().CountAsync(),
            ["DeductionTypes"] = await db.Set<DeductionType>().IgnoreQueryFilters().CountAsync(),
            ["EmployeePayProfiles"] = await db.Set<EmployeePayProfile>().IgnoreQueryFilters().CountAsync(),
            ["PayrollRuns"] = await db.Set<PayrollRun>().IgnoreQueryFilters().CountAsync(),
            ["SalaryLines"] = await db.Set<SalaryLine>().IgnoreQueryFilters().CountAsync(),
        });

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string url, HttpContent? content = null)
    {
        using var response = await client.PostAsync(url, content);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.TrimStart().StartsWith('{') ? JsonSerializer.Deserialize<JsonElement>(text) : default);
    }

    private static string Error(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var error) ? error.GetString() ?? "" : "";

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task An_empty_database_reaches_its_first_calculation_by_import_approval_attestation_and_publication_only()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        // HR.Core is another service: its answer to "is anyone blocked from pay?" is a double that says no one is, as in every
        // payroll calculation test. Everything of Payroll itself is the product's.
        await using var host = await Pay054FrameworkHttpTests.StartAsync(postgres, moreServices: services =>
        {
            services.RemoveAll<IHrCoreServiceRegimeClient>();
            services.AddScoped<IHrCoreServiceRegimeClient, FakeHrCoreServiceRegimeClient>();
        });
        using var processor = await Pay054FrameworkHttpTests.WithApiTokenAsync(host.Central());
        using var approver = await Pay054FrameworkHttpTests.WithApiTokenAsync(host.Restricted());
        using var attestor = await Pay054FrameworkHttpTests.WithApiTokenAsync(Pay054FrameworkHttpTests.Person(
            host, AttestorUser, $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}"));

        // 1. Empty: the migrations ran, no seed did.
        var empty = await RowsAsync(host);
        Assert.All(empty, table => Assert.True(table.Value == 0, $"{table.Key}: {table.Value} rows in a database with Seed:Profile=None"));
        Assert.Contains("data-testid=\"no-published-framework\"", await Pay054FrameworkHttpTests.PageAsync(processor, "/Dashboard"), StringComparison.Ordinal);

        // 2. Three employees, through the consumers. The second joins on Monday 5 October: his month is prorated.
        var employees = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }.OrderBy(id => id).ToArray();
        await HireAsync(host, employees[0], 40_000m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await HireAsync(host, employees[1], 65_000.50m, new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));
        await HireAsync(host, employees[2], 120_000m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var profiles = await host.WithDbAsync(db => db.Set<EmployeePayProfile>().AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.EmployeeId).ToListAsync());
        Assert.Equal(employees, profiles.Select(p => p.EmployeeId).ToArray());
        Assert.Equal([40_000m, 65_000.50m, 120_000m], profiles.Select(p => p.BaseSalary).ToArray());

        // 3. The month's run exists, and its calculation is refused: nothing governs October yet. The answer says where to start.
        var (created, createdBody) = await PostAsync(processor, "/api/payroll/runs", JsonContent.Create(new
        {
            periodStart = October,
            periodEnd = OctoberEnd,
            runDate = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc),
        }));
        Assert.Equal(HttpStatusCode.Created, created);
        var run = createdBody.GetProperty("id").GetGuid();
        var (refused, refusedBody) = await PostAsync(processor, $"/api/payroll/runs/{run:D}/calculate");
        Assert.Equal(HttpStatusCode.BadRequest, refused);
        Assert.Equal(PayrollFrameworkMessages.NoFrameworkForPeriod, Error(refusedBody));
        Assert.Contains(PayrollFrameworkMessages.ImportPath, Error(refusedBody), StringComparison.Ordinal);
        Assert.Equal(0, (await RowsAsync(host))["SalaryLines"]);

        // 4. A refused pack leaves no row, in any table.
        var beforePack = await RowsAsync(host);
        var (malformed, malformedBody) = await PostAsync(processor, "/api/payroll/frameworks/import",
            Pay054FrameworkHttpTests.ImportBody(Pay054FrameworkHttpTests.PackWith(p => p["taxBrackets"]![2]!["ratePercent"] = 120), effectiveFrom: "2026-10-01"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed);
        Assert.Equal("$.taxBrackets[2].ratePercent", malformedBody.GetProperty("errors")[0].GetProperty("path").GetString());
        Assert.Equal(beforePack, await RowsAsync(host));

        // 5. The framework, by the product's cycle and three people: imported, approved, attested, published.
        var (imported, importedBody) = await PostAsync(processor, "/api/payroll/frameworks/import",
            Pay054FrameworkHttpTests.ImportBody(Pay054FrameworkHttpTests.InterimPackJson(), effectiveFrom: "2026-10-01"));
        Assert.Equal(HttpStatusCode.Created, imported);
        var id = importedBody.GetProperty("frameworkId").GetGuid();
        // Still a draft: it governs nothing, and the calculation is still refused.
        Assert.Equal(PayrollFrameworkMessages.NoFrameworkForPeriod, Error((await PostAsync(processor, $"/api/payroll/runs/{run:D}/calculate")).Body));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(approver, $"/api/payroll/frameworks/{id:D}/approve")).Status);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(attestor, $"/api/payroll/frameworks/{id:D}/legal-attest")).Status);
        Assert.Equal(PayrollFrameworkMessages.NoFrameworkForPeriod, Error((await PostAsync(processor, $"/api/payroll/runs/{run:D}/calculate")).Body));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(approver, $"/api/payroll/frameworks/{id:D}/publish")).Status);

        var version = await host.WithDbAsync(db => db.Set<PayrollConfiguration>().AsNoTracking().Include(c => c.StatutoryDeductions).SingleAsync(c => c.Id == id));
        Assert.Equal(PayrollConfigPublicationStatus.Published, version.PublicationStatus);
        Assert.Equal(PayrollConfiguration.CurrentHashSchemaVersion, version.HashSchemaVersion);
        Assert.Equal(October, version.EffectiveFrom);

        // 6. The first calculation.
        var (calculated, calculatedBody) = await PostAsync(processor, $"/api/payroll/runs/{run:D}/calculate");
        Assert.True(calculated == HttpStatusCode.OK, $"the first calculation answered {(int)calculated}: {Error(calculatedBody)}");
        Assert.Equal(3, calculatedBody.GetProperty("linesGenerated").GetInt32());

        var (pinned, lines, components) = await host.WithDbAsync(async db =>
        {
            var row = await db.Set<PayrollRun>().AsNoTracking().SingleAsync(r => r.Id == run);
            var salaryLines = await db.Set<SalaryLine>().AsNoTracking().Where(l => l.PayrollRunId == run).OrderBy(l => l.EmployeeId).ToListAsync();
            var lineIds = salaryLines.Select(l => l.Id).ToList();
            var all = await db.Set<SalaryLineComponent>().AsNoTracking().Where(c => lineIds.Contains(c.SalaryLineId)).ToListAsync();
            return (row, salaryLines, all);
        });
        Assert.Equal(PayrollRunStatus.Calculated, pinned.Status);
        Assert.Equal(id, pinned.PayrollConfigurationId);
        Assert.Equal(version.ContentHash, pinned.ConfigContentHash);
        Assert.Equal(employees, lines.Select(l => l.EmployeeId).ToArray());
        Assert.Equal(40_000m, lines[0].GrossPay);
        Assert.Equal(120_000m, lines[2].GrossPay);
        Assert.InRange(lines[1].GrossPay, 1m, 65_000m);                                   // joined on the 5th: less than the month

        var rules = version.StatutoryDeductions.ToDictionary(r => r.Id);
        Assert.Equal(4, rules.Count);
        foreach (var line in lines)
        {
            var statutory = components.Where(c => c.SalaryLineId == line.Id && c.ReferenceId is Guid reference && rules.ContainsKey(reference)).ToList();
            Assert.Equal(4, statutory.Count);
            foreach (var component in statutory)
            {
                var rule = rules[component.ReferenceId!.Value];
                Assert.Equal(rule.NameAr, component.ComponentName);
                Assert.Equal(-version.RoundMoney(line.GrossPay * rule.EmployeePercent / 100m), component.Amount);
            }

            Assert.Equal(line.GrossPay - line.TotalDeductions, line.NetPay);
            Assert.True(line.NetPay > 0, "a first calculation that pays nothing");
        }

        // 7. Nothing came from a general table, because there is none: the version is the only source.
        var after = await RowsAsync(host);
        Assert.Equal(0, after["DeductionTypes"]);
        Assert.Equal(1, after["PayrollConfigurations"]);
        Assert.Equal(0, await host.WithDbAsync(db => db.Set<OfficialHoliday>().CountAsync(h => h.PayrollConfigurationId != id)));
        Assert.Equal(0, await host.WithDbAsync(db => db.Set<DisbursementPriorityRule>().CountAsync(r => r.PayrollConfigurationId != id)));
        Assert.Equal(0, await host.WithDbAsync(db => db.Set<TaxBracket>().CountAsync(b => b.PayrollConfigurationId != id)));

        using var provenance = JsonDocument.Parse(await processor.GetStringAsync($"/api/payroll/runs/{run:D}/provenance"));
        Assert.Equal(id, provenance.RootElement.GetProperty("payrollConfigurationId").GetGuid());
        Assert.Equal(2, provenance.RootElement.GetProperty("frameworkHashSchemaVersion").GetInt32());
        Assert.StartsWith("إطار مؤقت — استثناء ", provenance.RootElement.GetProperty("interimIndicator").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"no-published-framework\"", await Pay054FrameworkHttpTests.PageAsync(processor, "/Dashboard"), StringComparison.Ordinal);
    }
}
