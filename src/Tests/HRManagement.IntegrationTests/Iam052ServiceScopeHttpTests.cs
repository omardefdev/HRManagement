using System.Net.Http.Headers;
using System.Reflection;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Infrastructure.OrgScope;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.OrgScope;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-52 (P4): a service (PostgreSQL only) in process on a Testcontainers database of its own: three org unit snapshots (an HQ, the
/// restricted user's branch and another), one record of every registered type in each branch, the test authentication.
/// </summary>
public sealed class Iam052PgServiceHost<TEntry, TContext> : IIam052ScopeHost, IAsyncDisposable
    where TEntry : class
    where TContext : DbContext
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid RestrictedUser = Guid.Parse("52000000-0000-4000-8000-0000000000d1");
    private static readonly Guid CentralUser = Guid.Parse("52000000-0000-4000-8000-0000000000d2");
    private readonly Dictionary<Type, (Guid Own, Guid Foreign)> _records = new();
    private string _restrictedRoles = "";
    private string _centralRoles = "";

    private Iam052PgServiceHost(OrgScopeKeyRegistry registry, string tokenPage)
    {
        Registry = registry;
        TokenPage = tokenPage;
    }

    public WebApplicationFactory<TEntry> Host { get; private set; } = null!;

    public Assembly WebAssembly => typeof(TEntry).Assembly;

    public OrgScopeKeyRegistry Registry { get; }

    public Guid OwnUnit { get; private set; }

    public Guid ForeignUnit { get; private set; }

    public IReadOnlyDictionary<Type, (Guid Own, Guid Foreign)> Records => _records;

    public string TokenPage { get; }

    public static async Task<Iam052PgServiceHost<TEntry, TContext>> StartAsync(
        PostgreSqlFixture postgres,
        string connectionName,
        OrgScopeKeyRegistry registry,
        string restrictedRoles,
        string centralRoles,
        string tokenPage,
        Func<TContext, Guid, Task<Dictionary<Type, Guid>>> seed,
        Action<IServiceCollection>? testServices = null)
    {
        var host = new Iam052PgServiceHost<TEntry, TContext>(registry, tokenPage) { _restrictedRoles = restrictedRoles, _centralRoles = centralRoles };
        var cs = await postgres.CreateDatabaseAsync(TestDatabaseNames.Unique($"iam052_{typeof(TContext).Name.ToLowerInvariant()}_", 50));
        var builderOfOptions = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(typeof(TContext)))!;
        var options = builderOfOptions.UseNpgsql(cs, n => n.MigrationsAssembly(typeof(TContext).Assembly.GetName().Name)).Options;

        TenantIdFlow.Set(Tenant);
        try
        {
            await using var db = (TContext)Activator.CreateInstance(typeof(TContext), options, null)!;
            await db.Database.MigrateAsync();
            var hq = Snapshot("HQ", null, null);
            var own = Snapshot("B1", hq.Id, hq.MaterializedPath);
            var foreign = Snapshot("B2", hq.Id, hq.MaterializedPath);
            db.Set<OrgUnitSnapshot>().AddRange(hq, own, foreign);
            await db.SaveChangesAsync();
            (host.OwnUnit, host.ForeignUnit) = (own.Id, foreign.Id);
            var mine = await seed(db, own.Id);
            var theirs = await seed(db, foreign.Id);
            foreach (var (type, id) in mine)
                host._records[type] = (id, theirs[type]);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }

        host.Host = new WebApplicationFactory<TEntry>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting($"ConnectionStrings:{connectionName}", cs);
            // Settings the services read while Program.cs registers its services, before ConfigureAppConfiguration applies.
            builder.UseSetting("HrGateway:InternalBaseUrl", "http://localhost:5200");
            builder.UseSetting("Integration:HrCoreBaseUrl", "http://localhost:5101");
            builder.UseSetting("Keycloak:Authority", "http://localhost:8080/realms/hrmanagement");
            builder.UseSetting("HrAuth:BehindGateway", "true");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsAtStartup"] = "false",
                [$"ConnectionStrings:{connectionName}"] = cs,
                ["HrAuth:BehindGateway"] = "true",
                ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                ["HrGateway:InternalBaseUrl"] = "http://localhost:5200",
                ["Integration:HrCoreBaseUrl"] = "http://localhost:5101",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();
                testServices?.Invoke(services);
            });
        });
        return host;
    }

    private static OrgUnitSnapshot Snapshot(string code, Guid? parent, string? parentPath)
    {
        var unit = new OrgUnitSnapshot { Id = Guid.NewGuid(), TenantId = Tenant, ParentId = parent, Code = $"{code}-52", NameAr = code };
        unit.MaterializedPath = $"{parentPath ?? "/"}{unit.Id:D}/";
        return unit;
    }

    public async Task<T> WithDbAsync<T>(Func<DbContext, Task<T>> work)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        TenantIdFlow.Set(Tenant);
        try
        {
            return await work(scope.ServiceProvider.GetRequiredService<TContext>());
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private HttpClient Client(Guid user, string roles, Guid? scope, Guid? employee = null)
    {
        var client = Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Tenant.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        if (scope is Guid unit)
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeOrgUnitHeader, unit.ToString("D"));
        if (employee is Guid id)
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmployeeIdHeader, id.ToString("D"));
        return client;
    }

    public HttpClient Restricted() => Client(RestrictedUser, _restrictedRoles, OwnUnit);

    public HttpClient RestrictedWith(string roles, Guid scope, Guid employee) => Client(RestrictedUser, roles, scope, employee);

    public HttpClient Central() => Client(CentralUser, _centralRoles, null);

    /// <summary>An unrestricted caller with the given roles and employee (a delegate with no scope of his own).</summary>
    public HttpClient CentralWith(string roles, Guid employee) => Client(CentralUser, roles, null, employee);

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>
/// PLAN-52 (P4): HR.Contracts and Admin.Logistics, generated from their registries as HR.Core's: a foreign record by every keyed id of every
/// <c>[OrgScoped]</c> action answers 404 (or the kept 403) and changes nothing, and no list shows one. The restricted user holds
/// branch-manager with the service's own roles, so the latent controllers (HR.Contracts outside the hire wizard) are exercised too.
/// </summary>
[Collection("PostgreSql")]
public sealed class Iam052ServiceScopeHttpTests(PostgreSqlFixture postgres, ITestOutputHelper output)
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;

    private void Report(Iam052ScopeHttp.Outcome outcome, IIam052ScopeHost host)
    {
        foreach (var line in outcome.Exercised)
            output.WriteLine("ok   " + line);
        foreach (var line in outcome.Skipped)
            output.WriteLine("skip " + line);
        var unseeded = host.Registry.Keys.Keys.Where(t => !host.Records.ContainsKey(t)).Select(t => t.Name).ToList();
        Assert.True(unseeded.Count == 0, "No seed for: " + string.Join(", ", unseeded));
        Assert.True(outcome.Offenders.Count == 0, string.Join("\n", outcome.Offenders));
    }

    private static async Task<Dictionary<Type, Guid>> SeedContractsAsync(ContractsDbContext db, Guid unit)
    {
        var employee = new EmployeeOrgUnitSnapshot { EmployeeId = Guid.NewGuid(), TenantId = Tenant, PrimaryOrgUnitId = unit };
        db.EmployeeOrgUnitSnapshots.Add(employee);
        var type = new ContractType { TenantId = Tenant, Code = $"CT{Guid.NewGuid():N}"[..10], NameAr = "عقد" };
        var contract = new Contract { TenantId = Tenant, EmployeeId = employee.EmployeeId, ContractTypeId = type.Id, StartDate = new DateOnly(2026, 1, 1) };
        var today = new DateOnly(2026, 2, 1);
        var records = new List<BaseEntity>
        {
            type,
            contract,
            new ServiceTransaction { TenantId = Tenant, EmployeeId = employee.EmployeeId, TransactionType = ServiceTransactionType.Appointment },
            new LeaveBalance { TenantId = Tenant, EmployeeId = employee.EmployeeId, Year = 2026, TotalEntitlement = 30 },
            new LeaveRequest { TenantId = Tenant, EmployeeId = employee.EmployeeId, StartDate = today, EndDate = today, DurationDays = 1 },
            new AdvanceRequest { TenantId = Tenant, EmployeeId = employee.EmployeeId, Amount = 100 },
            new CommencementRecord { TenantId = Tenant, EmployeeId = employee.EmployeeId, StartDate = today },
            new ProbationRecord { TenantId = Tenant, EmployeeId = employee.EmployeeId, ContractId = contract.Id, StartDate = today, EndDate = today.AddMonths(3) },
            new SeparationRecord { TenantId = Tenant, EmployeeId = employee.EmployeeId, EffectiveDate = today },
            new AdministrativeDecision { TenantId = Tenant, Subject = "قرار", DecisionNumber = $"D-{Guid.NewGuid():N}"[..12], RelatedEmployeeId = employee.EmployeeId },
            new ContractSignature { TenantId = Tenant, ContractId = contract.Id, SignedBy = Guid.NewGuid(), SignedAt = DateTime.UtcNow },
        };
        db.AddRange(records);
        await db.SaveChangesAsync();
        var result = records.Where(r => r is not ContractType).ToDictionary(r => r.GetType(), r => r.Id);
        result[typeof(EmployeeOrgUnitSnapshot)] = employee.EmployeeId;
        return result;
    }

    private static async Task<Dictionary<Type, Guid>> SeedLogisticsAsync(AdminLogisticsDbContext db, Guid unit)
    {
        var employee = new EmployeeOrgUnitSnapshot { EmployeeId = Guid.NewGuid(), TenantId = Tenant, PrimaryOrgUnitId = unit };
        db.EmployeeOrgUnitSnapshots.Add(employee);
        var order = new SupplyOrder { TenantId = Tenant, OrgUnitId = unit, Items = "items", OrderedAt = DateTime.UtcNow };
        var asset = new Asset { TenantId = Tenant, TagNumber = $"T{Guid.NewGuid():N}"[..10], Name = "asset", Status = AssetStatus.InUse };
        var assignment = new AssetAssignment
        {
            TenantId = Tenant,
            AssetId = asset.Id,
            EmployeeId = employee.EmployeeId,
            AssignedAt = DateTime.UtcNow,
            Status = AssetAssignmentStatus.Active
        };
        var records = new List<BaseEntity>
        {
            order,
            new SupplyOrderLine { TenantId = Tenant, SupplyOrderId = order.Id, QtyRequested = 1 },
            new ReceiptRecord { TenantId = Tenant, SupplyOrderId = order.Id, ReceivedAt = DateTime.UtcNow },
            asset,
            assignment,
            new MaintenanceRecord { TenantId = Tenant, AssetId = asset.Id },
            new AssetLossReport { TenantId = Tenant, AssetId = asset.Id },
        };
        db.AddRange(records);
        await db.SaveChangesAsync();
        var result = records.ToDictionary(r => r.GetType(), r => r.Id);
        result[typeof(EmployeeOrgUnitSnapshot)] = employee.EmployeeId;
        return result;
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Contracts_a_foreign_record_is_404_or_the_kept_403_on_every_scoped_action_and_no_list_shows_one()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await Iam052PgServiceHost<HR.Contracts.Web.Controllers.HireWizardController, ContractsDbContext>.StartAsync(
            postgres, "ContractsDb", ContractsOrgScopeKeys.Registry,
            $"{HrManagementRoles.BranchManager},{HrManagementRoles.HrContracts},{HrManagementRoles.HrCore}",
            $"{HrManagementRoles.HrContracts},{HrManagementRoles.HrCore}",
            "/LeaveRequests/Create",
            SeedContractsAsync);
        Report(await Iam052ScopeHttp.RunAsync(host), host);

        // Review 3 (D-52-1): a restricted user's decision without an employee would be central; it is refused and no row is written. A
        // central user's is accepted.
        async Task<int> CountAsync(string number) => await host.WithDbAsync(async db =>
            await ((ContractsDbContext)db).AdministrativeDecisions.CountAsync(d => d.DecisionNumber == number));
        foreach (var (client, number, expected) in new[] { (host.Restricted(), "IAM52-R", 0), (host.Central(), "IAM52-C", 1) })
        {
            using (client)
            {
                var page = await client.GetAsync("/AdministrativeDecisions/Create");
                var token = System.Text.RegularExpressions.Regex.Match(await page.Content.ReadAsStringAsync(),
                    "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
                using var response = await client.PostAsync("/AdministrativeDecisions/Create", new FormUrlEncodedContent(
                [
                    new("__RequestVerificationToken", System.Net.WebUtility.HtmlDecode(token)), new("Subject", "IAM-52"), new("DecisionNumber", number),
                    new("IssuedDate", "2026-02-01")
                ]));
                Assert.True((int)response.StatusCode < 500, $"{number}: {(int)response.StatusCode}");
                Assert.Equal(expected, await CountAsync(number));
            }
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Logistics_a_foreign_record_is_404_or_the_kept_403_on_every_scoped_action_and_no_list_shows_one()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await Iam052PgServiceHost<AdminLogisticsWebProgram, AdminLogisticsDbContext>.StartAsync(
            postgres, "AdminDb", AdminLogisticsOrgScopeKeys.Registry,
            $"{HrManagementRoles.BranchManager},{HrManagementRoles.AdminLogistics}",
            HrManagementRoles.AdminLogistics,
            "/SupplyOrders/Create",
            SeedLogisticsAsync);
        Report(await Iam052ScopeHttp.RunAsync(host), host);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Logistics_reference_data_is_read_by_everyone_and_written_centrally()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await Iam052PgServiceHost<AdminLogisticsWebProgram, AdminLogisticsDbContext>.StartAsync(
            postgres, "AdminDb", AdminLogisticsOrgScopeKeys.Registry,
            $"{HrManagementRoles.BranchManager},{HrManagementRoles.AdminLogistics}",
            HrManagementRoles.AdminLogistics,
            "/SupplyOrders/Create",
            SeedLogisticsAsync);
        using var restricted = host.Restricted();
        Assert.Equal(System.Net.HttpStatusCode.OK, (await restricted.GetAsync("/InventoryItems")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await restricted.GetAsync("/Vendors")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await restricted.GetAsync("/InventoryItems/Create")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await restricted.GetAsync("/Vendors/Create")).StatusCode);
        using var central = host.Central();
        Assert.Equal(System.Net.HttpStatusCode.OK, (await central.GetAsync("/Vendors/Create")).StatusCode);
    }
}
