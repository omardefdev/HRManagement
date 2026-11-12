using System.Net.Http.Headers;
using System.Reflection;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.OrgScope;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-52 (P3): the in-process HR.Core host on SQLite, an HQ with two branches, and one record of every registered type in each.</summary>
public sealed class Iam052HrCoreScopeHost : IIam052ScopeHost, IAsyncLifetime
{
    private static readonly Guid RestrictedUser = Guid.Parse("52000000-0000-4000-8000-0000000000c1");
    private static readonly Guid CentralUser = Guid.Parse("52000000-0000-4000-8000-0000000000c2");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hr-iam052-core-{Guid.NewGuid():N}.db");
    private readonly Dictionary<Type, (Guid Own, Guid Foreign)> _records = new();

    public WebApplicationFactory<HRCoreWebProgram> Host { get; private set; } = null!;

    public Assembly WebAssembly => typeof(HRCoreWebProgram).Assembly;

    public OrgScopeKeyRegistry Registry => HrCoreOrgScopeKeys.Registry;

    public Guid Hq { get; private set; }

    public Guid OwnUnit { get; private set; }

    public Guid ForeignUnit { get; private set; }

    public IReadOnlyDictionary<Type, (Guid Own, Guid Foreign)> Records => _records;

    public string TokenPage => "/DailyAttendanceRecords/Create";

    public async Task InitializeAsync()
    {
        Host = new WebApplicationFactory<HRCoreWebProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recruitment:Jobs:IntakeCloseEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:OfferExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:ReserveExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateNotificationsEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateRetentionEnabled", "false");
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:HRCoreDb", $"Data Source={_dbPath}");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsAtStartup"] = "false",
                ["ConnectionStrings:HRCoreDb"] = $"Data Source={_dbPath}",
                ["HrAuth:BehindGateway"] = "true",
                ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                ["HrGateway:InternalBaseUrl"] = "http://localhost:5200",
                ["NationalScope:IsLabScale"] = "false"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();
            });
        });

        await WithDbAsync(async db =>
        {
            var core = (HRCoreDbContext)db;
            await core.Database.EnsureCreatedAsync();
            await LegislationRegisterSeeder.SeedAsync(core);
            await WorkflowPackSeeder.SeedAsync(core);
            var hq = Unit("HQ", null, null);
            var own = Unit("B1", hq.Id, hq.MaterializedPath);
            var foreign = Unit("B2", hq.Id, hq.MaterializedPath);
            core.OrgUnits.AddRange(hq, own, foreign);
            await core.SaveChangesAsync();
            (Hq, OwnUnit, ForeignUnit) = (hq.Id, own.Id, foreign.Id);
            _records[typeof(OrgUnit)] = (own.Id, foreign.Id);

            var shift = new Shift { TenantId = Tenant, Code = $"S{Guid.NewGuid():N}"[..8], NameAr = "وردية", ShiftType = ShiftType.Day, IsActive = true };
            core.Shifts.Add(shift);
            await core.SaveChangesAsync();
            var definition = await core.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking().FirstAsync(d => d.Code == "RECRUITMENT-REQUISITION-APPROVAL");

            var mine = await SeedBranchAsync(core, own.Id, shift.Id, definition);
            var theirs = await SeedBranchAsync(core, foreign.Id, shift.Id, definition);
            foreach (var (type, id) in mine)
                _records[type] = (id, theirs[type]);
            return true;
        });
    }

    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;

    private static OrgUnit Unit(string code, Guid? parent, string? parentPath)
    {
        var unit = new OrgUnit { TenantId = Tenant, Code = $"{code}-52-{Guid.NewGuid():N}"[..14], NameAr = code, NameEn = code, ParentId = parent };
        unit.MaterializedPath = $"{parentPath ?? "/"}{unit.Id:D}/";
        return unit;
    }

    /// <summary>One record of every registered type in <paramref name="unit"/>.</summary>
    private static async Task<Dictionary<Type, Guid>> SeedBranchAsync(HRCoreDbContext db, Guid unit, Guid shiftId, WorkflowDefinition definition)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var employee = new Employee
        {
            TenantId = Tenant,
            FirstName = "Emp",
            LastName = suffix,
            FullName = $"Emp {suffix}",
            NationalId = $"9{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PrimaryOrgUnitId = unit
        };
        db.Employees.Add(employee);
        var cv = new ApprovedCV { TenantId = Tenant, EmployeeId = employee.Id };
        var position = new FundedPosition
        {
            TenantId = Tenant,
            PositionCode = $"P52-{suffix}",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = unit
        };
        var requisition = new RecruitmentRequisition
        {
            TenantId = Tenant,
            RequisitionNumber = $"R52-{suffix}",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Draft,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        var application = new RecruitmentApplication
        {
            TenantId = Tenant,
            ApplicantFullName = $"App {suffix}",
            AppliedPosition = "محلل",
            AppliedAt = DateTime.UtcNow,
            RequisitionId = requisition.Id
        };
        var keyPosition = new KeyPosition { TenantId = Tenant, Title = $"KP {suffix}", OrgUnitId = unit, Criticality = 3 };
        var plan = new SuccessionPlan { TenantId = Tenant, KeyPositionId = keyPosition.Id, Year = 2026 };
        var roster = new ShiftRoster { TenantId = Tenant, Title = $"RO {suffix}", OrgUnitId = unit, PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 1, 31) };
        var workforcePlan = new WorkforcePlan { TenantId = Tenant, Title = $"WP {suffix}", FiscalYear = 2026, OrgUnitId = unit, Status = "Draft" };
        var vacancy = new InternalVacancy { TenantId = Tenant, Title = $"IV {suffix}", OrgUnitId = unit, IsOpen = true };
        var records = new List<object>
        {
            cv,
            position,
            requisition,
            application,
            keyPosition,
            plan,
            roster,
            workforcePlan,
            vacancy,
            new DailyAttendanceRecord { TenantId = Tenant, EmployeeId = employee.Id, AttendanceDate = new DateOnly(2026, 1, 5), Status = DailyAttendanceStatus.Present },
            new ContactInfo { TenantId = Tenant, EmployeeId = employee.Id, Type = HRManagement.SharedKernel.Enums.ContactType.Phone, Value = "0900000000" },
            new EmployeeDocument { TenantId = Tenant, EmployeeId = employee.Id, DocTitle = $"D {suffix}" },
            new EmployeeCorrection
            {
                TenantId = Tenant, EmployeeId = employee.Id, ChangesJson = "{}", Reason = "r", RequestedBy = Guid.NewGuid(), RequestedAt = DateTime.UtcNow,
                Status = HRManagement.SharedKernel.Enums.ApprovalStatus.Pending
            },
            new EmployeeProfile { TenantId = Tenant, EmployeeId = employee.Id },
            new EmploymentRecord { TenantId = Tenant, EmployeeId = employee.Id, PositionTitle = "محلل", StartDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), Status = "Active" },
            new PerformanceEvaluation { TenantId = Tenant, EmployeeId = employee.Id, Year = 2025, Score = 80, Status = "Draft" },
            new Qualification { TenantId = Tenant, EmployeeId = employee.Id, Title = $"Q {suffix}" },
            new EmploymentStatusHistory { TenantId = Tenant, EmployeeId = employee.Id, NewStatus = "Active", ChangedAt = DateTime.UtcNow },
            new ServiceRegimeException { TenantId = Tenant, EmployeeId = employee.Id },
            new ApprovalRequest
            {
                TenantId = Tenant, EntityType = "ApprovedCV", EntityId = cv.Id, RequestedAt = DateTime.UtcNow,
                Status = HRManagement.SharedKernel.Enums.ApprovalStatus.Pending, CurrentLevel = 1
            },
            new DiwanCorrespondence { TenantId = Tenant, ReferenceNumber = $"DW-{suffix}", Subject = "s", RegisteredAt = DateTime.UtcNow, RelatedEmployeeId = employee.Id, OrgUnitId = unit },
            new FieldInspectionVisit { TenantId = Tenant, Title = $"FI {suffix}", VisitDate = new DateOnly(2026, 1, 5), OrgUnitId = unit },
            new HrStatisticReport { TenantId = Tenant, Title = $"HS {suffix}", PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 1, 31), OrgUnitId = unit },
            new ShiftAssignment { TenantId = Tenant, ShiftRosterId = roster.Id, ShiftId = shiftId, EmployeeId = employee.Id, WorkDate = new DateOnly(2026, 1, 6) },
            new SuccessorCandidate { TenantId = Tenant, SuccessionPlanId = plan.Id, EmployeeId = employee.Id },
            new WorkforcePlanLine { TenantId = Tenant, WorkforcePlanId = workforcePlan.Id, PlannedCount = 1, PlannedFTE = 1 },
            new InternalApplication { TenantId = Tenant, InternalVacancyId = vacancy.Id, EmployeeId = employee.Id, AppliedAt = DateTime.UtcNow },
            new RecruitmentAppeal { TenantId = Tenant, RecruitmentApplicationId = application.Id, Reason = "r" },
            new RecruitmentInterviewSlot { TenantId = Tenant, RecruitmentApplicationId = application.Id, ScheduledAt = DateTime.UtcNow.AddDays(3) },
            new WorkflowInstance
            {
                TenantId = Tenant, DefinitionId = definition.Id, DefinitionVersion = definition.Version, EntityType = "RecruitmentRequisition",
                EntityId = requisition.Id, Status = WorkflowStatus.InProgress, StepHistoryJson = "[]", StartedAt = DateTime.UtcNow, ScopeOrgUnitId = unit
            },
        };
        db.AddRange(records);
        await db.SaveChangesAsync();

        var result = new Dictionary<Type, Guid> { [typeof(Employee)] = employee.Id };
        foreach (var record in records)
            result[record.GetType()] = (Guid)record.GetType().GetProperty("Id")!.GetValue(record)!;
        return result;
    }

    public async Task<T> WithDbAsync<T>(Func<DbContext, Task<T>> work)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        TenantIdFlow.Set(Tenant);
        try
        {
            return await work(scope.ServiceProvider.GetRequiredService<HRCoreDbContext>());
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private HttpClient Client(Guid user, string roles, Guid? scope)
    {
        var client = Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Tenant.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        if (scope is Guid unit)
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeOrgUnitHeader, unit.ToString("D"));
        return client;
    }

    public HttpClient Restricted() => Client(RestrictedUser, $"{HrManagementRoles.BranchManager},{HrManagementRoles.HrCore}", OwnUnit);

    public HttpClient Central() => Client(CentralUser, HrManagementRoles.HrCore, null);

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            if (global::System.IO.File.Exists(_dbPath))
                global::System.IO.File.Delete(_dbPath);
        }
        catch
        {
            // best effort temp cleanup
        }
    }
}

/// <summary>
/// PLAN-52 (P3): every <c>[OrgScoped]</c> controller of HR.Core, generated from its registry: a foreign record by every keyed id of every
/// action answers 404 (or the kept 403) and changes nothing; no list shows a foreign record; every registered type has a seed.
/// </summary>
public sealed class Iam052HrCoreScopeHttpTests(Iam052HrCoreScopeHost host, ITestOutputHelper output) : IClassFixture<Iam052HrCoreScopeHost>
{
    [Fact]
    public void Every_registered_type_has_a_seed_in_both_branches()
    {
        var missing = host.Registry.Keys.Keys.Where(t => !host.Records.ContainsKey(t)).Select(t => t.Name).ToList();
        Assert.True(missing.Count == 0, "No seed for: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task A_foreign_record_is_404_or_the_kept_403_on_every_scoped_action_and_no_list_shows_one()
    {
        var outcome = await Iam052ScopeHttp.RunAsync(host);
        foreach (var line in outcome.Exercised)
            output.WriteLine("ok   " + line);
        foreach (var line in outcome.Skipped)
            output.WriteLine("skip " + line);
        Assert.True(outcome.Offenders.Count == 0, string.Join("\n", outcome.Offenders));
        Assert.True(outcome.Exercised.Count > 50, $"only {outcome.Exercised.Count} checks ran");
    }
}
