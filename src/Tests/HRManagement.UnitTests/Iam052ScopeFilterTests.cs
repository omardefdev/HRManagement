using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-52 (P1, P-4): the one org-scope filter, rule by rule, on a SQLite context. Each test pairs the refusal with a control that must pass
/// (an in-scope id, an unrestricted user, a not-applicable action), so both a filter that lets the foreign id through and one that refuses
/// everything fail it; the planted defects (a key left out of the registry, an id of the wrong kind) fail closed.
/// </summary>
public sealed class Iam052ScopeFilterTests : IAsyncLifetime
{
    private static readonly Guid Branch1 = Guid.Parse("52000000-0000-4000-8000-000000000001");
    private static readonly Guid Branch1Section = Guid.Parse("52000000-0000-4000-8000-000000000011");
    private static readonly Guid Branch2 = Guid.Parse("52000000-0000-4000-8000-000000000002");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly CountingInterceptor _counter = new();
    private ScopeTestDb _db = null!;
    private TestRecord _own = null!;
    private TestRecord _foreign = null!;
    private TestRecord _central = null!;
    private TestChild _ownChild = null!;
    private TestChild _foreignChild = null!;
    private TestEmployee _ownEmployee = null!;
    private TestEmployee _foreignEmployee = null!;
    private TestTransfer _inside = null!;
    private TestTransfer _halfOutside = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ScopeTestDb(new DbContextOptionsBuilder<ScopeTestDb>().UseSqlite(_connection).AddInterceptors(_counter).Options);
        await _db.Database.EnsureCreatedAsync();
        _own = new TestRecord { OrgUnitId = Branch1Section };
        _foreign = new TestRecord { OrgUnitId = Branch2 };
        _central = new TestRecord { OrgUnitId = null };
        _db.Records.AddRange(_own, _foreign, _central);
        _ownChild = new TestChild { RecordId = _own.Id };
        _foreignChild = new TestChild { RecordId = _foreign.Id };
        _db.Children.AddRange(_ownChild, _foreignChild);
        _ownEmployee = new TestEmployee { UnitId = Branch1 };
        _foreignEmployee = new TestEmployee { UnitId = Branch2 };
        _db.Employees.AddRange(_ownEmployee, _foreignEmployee);
        _inside = new TestTransfer { FromUnitId = Branch1, ToUnitId = Branch1Section };
        _halfOutside = new TestTransfer { FromUnitId = Branch1, ToUnitId = Branch2 };
        _db.Transfers.AddRange(_inside, _halfOutside);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static OrgAccessScope Restricted() => OrgAccessScope.Restricted(new HashSet<Guid> { Branch1, Branch1Section });

    private static OrgScopeKeyRegistry Registry(bool withChildKey = true)
    {
        var registry = new OrgScopeKeyRegistry("unit-test")
            .Key<ScopeTestDb, TestRecord>(_ => r => r.OrgUnitId)
            .Key<ScopeTestDb, TestEmployee>(_ => e => e.UnitId)
            .Key<ScopeTestDb, TestTransfer>(_ => t => t.FromUnitId, _ => t => (Guid?)t.ToUnitId)
            .Id("employeeId", typeof(TestEmployee))
            .Id("memberId", typeof(TestEmployee))
            .Id("orgUnitId", typeof(OrgScopeUnit))
            .Id("childId", typeof(TestChild))
            .Id("transferId", typeof(TestTransfer));
        if (withChildKey)
            registry.Key<ScopeTestDb, TestChild>(db => c => db.Records.Where(r => r.Id == c.RecordId).Select(r => r.OrgUnitId).FirstOrDefault());
        return registry;
    }

    private async Task<(IActionResult? Result, bool Ran)> RunAsync<TController>(
        string action,
        OrgAccessScope scope,
        Dictionary<string, object?> arguments,
        OrgScopeKeyRegistry? registry = null)
    {
        var method = typeof(TController).GetMethod(action)!;
        var services = new ServiceCollection().AddSingleton<IOrgScopeProvider>(new FixedScope(scope)).BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test")),
        };
        var descriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(TController).GetTypeInfo(),
            MethodInfo = method,
            ControllerName = typeof(TController).Name,
            ActionName = action,
        };
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), descriptor), new List<IFilterMetadata>(), arguments, controller: new object());
        var ran = false;
        var filter = new OrgScopeActionFilter<ScopeTestDb>(_db, registry ?? Registry());
        await filter.OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), controller: new object()));
        });
        return (context.Result, ran);
    }

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value);

    private static void AssertNotFound((IActionResult? Result, bool Ran) outcome)
    {
        Assert.IsType<NotFoundResult>(outcome.Result);
        Assert.False(outcome.Ran, "the action must not run");
    }

    private static void AssertForbidden((IActionResult? Result, bool Ran) outcome)
    {
        Assert.IsType<ForbidResult>(outcome.Result);
        Assert.False(outcome.Ran, "the action must not run");
    }

    private static void AssertPassed((IActionResult? Result, bool Ran) outcome)
    {
        Assert.Null(outcome.Result);
        Assert.True(outcome.Ran, "the action must run");
    }

    [Fact]
    public async Task A_denied_scope_is_refused_with_403_and_a_valid_scope_passes()
    {
        AssertForbidden(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Details), OrgAccessScope.Denied(), Args(("id", _own.Id))));
        AssertForbidden(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Index), OrgAccessScope.Denied(), Args()));
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Details), Restricted(), Args(("id", _own.Id))));
    }

    [Fact]
    public async Task A_central_only_action_or_controller_is_refused_with_403_for_a_restricted_user_only()
    {
        AssertForbidden(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Central), Restricted(), Args()));
        AssertForbidden(await RunAsync<CentralController>(nameof(CentralController.Anything), Restricted(), Args(("id", _own.Id))));
        AssertPassed(await RunAsync<CentralController>(nameof(CentralController.Anything), OrgAccessScope.Unrestricted(), Args(("id", _own.Id))));
    }

    [Fact]
    public async Task A_foreign_id_is_404_before_the_action_runs_and_an_own_id_passes()
    {
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Details), Restricted(), Args(("id", _foreign.Id))));
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Delete), Restricted(), Args(("id", _foreign.Id))));
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Delete), Restricted(), Args(("id", _own.Id))));
    }

    [Fact]
    public async Task A_record_with_no_key_or_no_record_at_all_is_404()
    {
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Details), Restricted(), Args(("id", _central.Id))));
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Details), Restricted(), Args(("id", Guid.NewGuid()))));
    }

    [Fact]
    public async Task Every_property_of_a_bound_model_is_checked()
    {
        var foreignEmployee = new RecordForm { Id = _own.Id, EmployeeId = _foreignEmployee.Id, OrgUnitId = Branch1 };
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Edit), Restricted(), Args(("id", _own.Id), ("form", foreignEmployee))));
        var foreignUnit = new RecordForm { Id = _own.Id, EmployeeId = _ownEmployee.Id, OrgUnitId = Branch2 };
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Edit), Restricted(), Args(("id", _own.Id), ("form", foreignUnit))));
        var foreignModelId = new RecordForm { Id = _foreign.Id, EmployeeId = _ownEmployee.Id, OrgUnitId = Branch1 };
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Edit), Restricted(), Args(("id", _own.Id), ("form", foreignModelId))));
        var foreignMember = new RecordForm { Id = _own.Id, EmployeeId = _ownEmployee.Id, OrgUnitId = Branch1, MemberIds = [_ownEmployee.Id, _foreignEmployee.Id] };
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Edit), Restricted(), Args(("id", _own.Id), ("form", foreignMember))));
        var allOwn = new RecordForm { Id = _own.Id, EmployeeId = _ownEmployee.Id, OrgUnitId = Branch1Section, MemberIds = [_ownEmployee.Id] };
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Edit), Restricted(), Args(("id", _own.Id), ("form", allOwn))));
    }

    [Fact]
    public async Task Each_element_of_a_list_of_ids_is_checked()
    {
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Bulk), Restricted(), Args(("ids", new List<Guid> { _own.Id, _foreign.Id }))));
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Bulk), Restricted(), Args(("ids", new List<Guid> { _own.Id }))));
    }

    [Fact]
    public async Task A_parent_and_a_child_are_both_checked()
    {
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ChildOfParent), Restricted(), Args(("id", _own.Id), ("childId", _foreignChild.Id))));
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ChildOfParent), Restricted(), Args(("id", _foreign.Id), ("childId", _ownChild.Id))));
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ChildOfParent), Restricted(), Args(("id", _own.Id), ("childId", _ownChild.Id))));
    }

    [Fact]
    public async Task An_action_level_id_type_and_a_unit_and_an_employee_resolve_through_their_keys()
    {
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ChildDetails), Restricted(), Args(("id", _foreignChild.Id))));
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ChildDetails), Restricted(), Args(("id", _ownChild.Id))));
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ByUnit), Restricted(), Args(("orgUnitId", Branch2))));
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ByUnit), Restricted(), Args(("orgUnitId", Branch1Section))));
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ByEmployee), Restricted(), Args(("employeeId", _foreignEmployee.Id))));
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ByEmployee), Restricted(), Args(("employeeId", _ownEmployee.Id))));
    }

    [Fact]
    public async Task A_record_with_two_units_needs_both_in_the_scope()
    {
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Transfer), Restricted(), Args(("transferId", _halfOutside.Id))));
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Transfer), Restricted(), Args(("transferId", _inside.Id))));
    }

    [Fact]
    public async Task The_kept_answer_is_403_for_the_same_foreign_id()
    {
        AssertForbidden(await RunAsync<KeptAnswerController>(nameof(KeptAnswerController.Details), Restricted(), Args(("id", _foreign.Id))));
        AssertPassed(await RunAsync<KeptAnswerController>(nameof(KeptAnswerController.Details), Restricted(), Args(("id", _own.Id))));
        // An action-level stance without a type keeps the class's type and sets its own answer.
        AssertNotFound(await RunAsync<KeptAnswerController>(nameof(KeptAnswerController.Gone), Restricted(), Args(("id", _foreign.Id))));
    }

    [Fact]
    public async Task A_declared_non_key_and_a_not_applicable_action_are_not_checked()
    {
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.WithType), Restricted(), Args(("id", _own.Id), ("typeId", Guid.NewGuid()))));
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Open), Restricted(), Args(("id", _foreign.Id))));
        AssertPassed(await RunAsync<NotApplicableController>(nameof(NotApplicableController.Read), Restricted(), Args(("id", _foreign.Id))));
        AssertForbidden(await RunAsync<NotApplicableController>(nameof(NotApplicableController.Write), Restricted(), Args(("id", _foreign.Id))));
    }

    [Fact]
    public async Task An_unrestricted_user_costs_no_query_and_is_never_refused()
    {
        _counter.Commands = 0;
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ChildOfParent), OrgAccessScope.Unrestricted(), Args(("id", _foreign.Id), ("childId", _foreignChild.Id))));
        AssertPassed(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Edit), OrgAccessScope.Unrestricted(), Args(("id", _central.Id), ("form", new RecordForm { Id = _central.Id, EmployeeId = _foreignEmployee.Id, OrgUnitId = Branch2 }))));
        Assert.Equal(0, _counter.Commands);
        // The control: a restricted user does query.
        await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Details), Restricted(), Args(("id", _own.Id)));
        Assert.True(_counter.Commands > 0);
    }

    [Fact]
    public async Task Planted_defect_a_key_left_out_of_the_registry_fails_closed()
    {
        var defective = Registry(withChildKey: false);
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.ChildDetails), Restricted(), Args(("id", _ownChild.Id)), defective));
    }

    [Fact]
    public async Task Planted_defect_an_id_of_another_kind_is_404_not_a_pass()
    {
        // A child's id passed where the controller's own record is expected names no record of that type.
        AssertNotFound(await RunAsync<ScopedRecordsController>(nameof(ScopedRecordsController.Details), Restricted(), Args(("id", _ownChild.Id))));
    }

    [Fact]
    public void The_filter_runs_before_the_automatic_400_of_ApiController()
    {
        Assert.True(OrgScopeActionFilter<ScopeTestDb>.FilterOrder < -2000);
        Assert.Equal(OrgScopeActionFilter<ScopeTestDb>.FilterOrder, new OrgScopeActionFilter<ScopeTestDb>(_db, Registry()).Order);
    }

    [Fact]
    public async Task WhereInOrgScope_filters_lists_with_the_same_keys()
    {
        var registry = Registry();
        var restricted = await _db.Records.WhereInOrgScope(registry, Restricted(), _db).Select(r => r.Id).ToListAsync();
        Assert.Equal(new[] { _own.Id }, restricted);
        var children = await _db.Children.WhereInOrgScope(registry, Restricted(), _db).Select(c => c.Id).ToListAsync();
        Assert.Equal(new[] { _ownChild.Id }, children);
        var transfers = await _db.Transfers.WhereInOrgScope(registry, Restricted(), _db).Select(t => t.Id).ToListAsync();
        Assert.Equal(new[] { _inside.Id }, transfers);
        Assert.Empty(await _db.Records.WhereInOrgScope(registry, OrgAccessScope.Denied(), _db).ToListAsync());
        Assert.Equal(3, await _db.Records.WhereInOrgScope(registry, OrgAccessScope.Unrestricted(), _db).CountAsync());
    }

    [Fact]
    public void The_stance_and_the_ids_are_read_by_reflection_the_way_the_filter_reads_them()
    {
        var stance = OrgScopeActionStance.For(typeof(ScopedRecordsController), typeof(ScopedRecordsController).GetMethod(nameof(ScopedRecordsController.Edit))!);
        Assert.Equal(typeof(TestRecord), stance!.EntityType);
        var slots = OrgScopeIds.Slots(typeof(ScopedRecordsController), typeof(ScopedRecordsController).GetMethod(nameof(ScopedRecordsController.Edit))!,
            Registry(), stance.EntityType);
        Assert.Equal(new[] { "id", "form.Id", "form.EmployeeId", "form.OrgUnitId", "form.MemberIds" }, slots.Select(s => s.Path));
        Assert.All(slots, s => Assert.True(s.Rule is { IsKey: true }, s.Path));
    }

    private sealed class FixedScope(OrgAccessScope scope) : IOrgScopeProvider
    {
        public Task<OrgAccessScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken ct = default) => Task.FromResult(scope);
    }

    private sealed class CountingInterceptor : DbCommandInterceptor
    {
        public int Commands { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

public sealed class ScopeTestDb(DbContextOptions<ScopeTestDb> options) : DbContext(options)
{
    public DbSet<TestRecord> Records => Set<TestRecord>();

    public DbSet<TestChild> Children => Set<TestChild>();

    public DbSet<TestEmployee> Employees => Set<TestEmployee>();

    public DbSet<TestTransfer> Transfers => Set<TestTransfer>();
}

public sealed class TestRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? OrgUnitId { get; set; }
}

public sealed class TestChild
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RecordId { get; set; }
}

public sealed class TestEmployee
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? UnitId { get; set; }
}

public sealed class TestTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? FromUnitId { get; set; }

    public Guid ToUnitId { get; set; }
}

public sealed class RecordForm
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid? OrgUnitId { get; set; }

    public List<Guid> MemberIds { get; set; } = [];
}

[OrgScoped(typeof(TestRecord))]
public sealed class ScopedRecordsController : Controller
{
    public IActionResult Index() => Ok();

    public IActionResult Details(Guid id) => Ok(id);

    public IActionResult Delete(Guid id) => Ok(id);

    public IActionResult Edit(Guid id, RecordForm form) => Ok(form);

    public IActionResult Bulk(List<Guid> ids) => Ok(ids);

    public IActionResult ChildOfParent(Guid id, Guid childId) => Ok(childId);

    [OrgScopeId(typeof(TestChild))]
    public IActionResult ChildDetails(Guid id) => Ok(id);

    public IActionResult ByEmployee(Guid employeeId) => Ok(employeeId);

    public IActionResult ByUnit(Guid orgUnitId) => Ok(orgUnitId);

    public IActionResult Transfer(Guid transferId) => Ok(transferId);

    public IActionResult WithType(Guid id, [NotOrgScopeKey("a reference type")] Guid typeId) => Ok(typeId);

    [OrgScopeCentralOnly]
    public IActionResult Central() => Ok();

    [OrgScopeNotApplicable("a page with no org records")]
    public IActionResult Open(Guid id) => Ok(id);
}

[OrgScoped(typeof(TestRecord), OutOfScope = OrgScopeAnswer.Forbid)]
public sealed class KeptAnswerController : Controller
{
    public IActionResult Details(Guid id) => Ok(id);

    [OrgScoped(OutOfScope = OrgScopeAnswer.NotFound)]
    public IActionResult Gone(Guid id) => Ok(id);
}

[OrgScopeCentralOnly]
public sealed class CentralController : Controller
{
    public IActionResult Anything(Guid id) => Ok(id);
}

[OrgScopeNotApplicable("reference data")]
public sealed class NotApplicableController : Controller
{
    public IActionResult Read(Guid id) => Ok(id);

    [OrgScopeCentralOnly]
    public IActionResult Write(Guid id) => Ok(id);
}
