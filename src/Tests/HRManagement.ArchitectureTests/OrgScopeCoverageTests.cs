using System.Reflection;
using HRManagement.ServiceDefaults;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-52 (§6, P-11): BOLA v2, by reflection over the fifteen web assemblies and the shared controllers (it replaces v1's regex over the
/// files).
/// <list type="number">
/// <item>Every controller declares its scope stance (<see cref="IOrgScopeStance"/>: <see cref="OrgScopedAttribute"/>,
/// <see cref="OrgScopeCentralOnlyAttribute"/>, <see cref="OrgScopeNotApplicableAttribute"/> or <c>[RecruitmentOrgScope]</c>), or is a row of
/// its service's list. A row is latent (no restricted user reaches it, <see cref="OrgScopeReach"/>) and signed <c>IAM-H</c>; HR.Core,
/// HR.Contracts and Admin.Logistics have no list.</item>
/// <item>In an <see cref="OrgScopedAttribute"/> action, GET and POST alike, every <c>Guid</c>/<c>Guid?</c> parameter or bound-model property
/// named <c>id</c> or <c>…Id</c>, and every list of <c>Guid</c> named <c>ids</c> or <c>…Ids</c>, has a key in the service's registry, or is
/// declared not a key with a reason.</item>
/// <item>The registries are built by each service's own <c>{Service}OrgScopeKeys.Configure</c>, called here for real.</item>
/// </list>
/// A list only shrinks: a row whose controller declares its stance must go, and a ceiling only goes down.
/// </summary>
public sealed class OrgScopeCoverageTests
{
    public static string ListPath(string service) => $"tests/architecture/org-scope-allowlist/{service}.txt";

    /// <summary>The owner of a latent row (wave 2: the scope as a property of any role, D-IAM-3).</summary>
    public const string LatentOwner = "IAM-H";

    /// <summary>The services whose every controller declares its stance (P-11): no list.</summary>
    public static readonly IReadOnlySet<string> FullyDeclared = new HashSet<string>(["hr-core", "hr-contracts", "admin-logistics"], StringComparer.Ordinal);

    /// <summary>The most rows each list may hold. Lower a ceiling when its list shrinks; never raise one.</summary>
    public static readonly IReadOnlyDictionary<string, int> Ceilings = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["hr-core"] = 0,
        ["hr-contracts"] = 0,
        ["admin-logistics"] = 0,
        ["payroll"] = 24,
        ["hr-movements"] = 8,
        ["hr-training"] = 13,
        ["hr-health"] = 11,
        ["compliance"] = 11,
        ["idcard"] = 7,
        ["it-infra"] = 8,
        ["records-archive"] = 7,
        ["dev-analytics"] = 12,
        ["auth-directory"] = 8,
        ["file-storage"] = 2,
        ["gateway"] = 3,
        ["shared"] = 0,
    };

    /// <summary>The registries, as each service's Program.cs builds them.</summary>
    public static readonly IReadOnlyDictionary<string, Action<OrgScopeKeyRegistry>> Configurations =
        new Dictionary<string, Action<OrgScopeKeyRegistry>>(StringComparer.Ordinal)
        {
            ["hr-core"] = HR.Core.Infrastructure.OrgScope.HrCoreOrgScopeKeys.Configure,
            ["hr-contracts"] = HR.Contracts.Infrastructure.OrgScope.ContractsOrgScopeKeys.Configure,
            ["admin-logistics"] = Admin.Logistics.Infrastructure.OrgScope.AdminLogisticsOrgScopeKeys.Configure,
            ["hr-movements"] = HR.Movements.Infrastructure.OrgScope.MovementsOrgScopeKeys.Configure,
        };

    public static OrgScopeKeyRegistry? RegistryOf(string service)
    {
        if (!Configurations.TryGetValue(service, out var configure))
            return null;
        var registry = new OrgScopeKeyRegistry(service);
        configure(registry);
        return registry;
    }

    /// <summary>A controller declares its stance on its class, or on every one of its actions.</summary>
    public static bool Declares(string service, Type controller) =>
        controller.GetCustomAttributes(inherit: true).OfType<IOrgScopeStance>().Any()
        || ActionCatalog.ReadController(service, controller).All(a => a.Method.GetCustomAttributes(inherit: true).OfType<IOrgScopeStance>().Any());

    public static string NameOf(Type controller) =>
        controller.Name.EndsWith("Controller", StringComparison.Ordinal) ? controller.Name[..^"Controller".Length] : controller.Name;

    public static IEnumerable<string> Undeclared(string service, IEnumerable<Type> controllers) =>
        controllers.Where(c => !Declares(service, c)).Select(NameOf).Distinct(StringComparer.Ordinal);

    /// <summary>Rule 2: the ids of an <see cref="OrgScopedAttribute"/> action without a key or a declaration, and the keys the registry lacks.</summary>
    public static IEnumerable<string> UnkeyedIds(string service, IEnumerable<Type> controllers, OrgScopeKeyRegistry? registry)
    {
        foreach (var controller in controllers)
            foreach (var action in ActionCatalog.ReadController(service, controller))
            {
                if (OrgScopeActionStance.For(controller, action.Method) is not { Stance: OrgScopedAttribute } stance)
                    continue;
                if (stance.EntityType is { } entity && registry?.HasKey(entity) != true)
                    yield return $"{action.Key}: the stance names {entity.Name}, which has no key in the {service} registry";
                foreach (var slot in OrgScopeIds.Slots(controller, action.Method, registry, stance.EntityType).Where(s => s.IsConventional))
                {
                    if (slot.Rule is null)
                        yield return $"{action.Key}({slot.Path}): no key and no [NotOrgScopeKey]";
                    else if (slot.Rule.IsKey && registry?.HasKey(slot.Rule.EntityType!) != true)
                        yield return $"{action.Key}({slot.Path}): {slot.Rule.EntityType!.Name} has no key in the {service} registry";
                }
            }
    }

    public static TheoryData<string> Services() => new(WebAssemblyCatalog.Entries.Select(e => e.Service));

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_controller_declares_its_scope_stance_or_is_a_row_of_its_list(string service)
    {
        var unlisted = CeilingChecks.Unlisted(Undeclared(service, WebAssemblyCatalog.Controllers(service)), CeilingList.Load(ListPath(service)));
        Assert.True(unlisted.Count == 0,
            $"{service}: these controllers declare no scope stance ([OrgScoped], [OrgScopeCentralOnly], [OrgScopeNotApplicable(reason)] or "
            + "[RecruitmentOrgScope]); a new row is refused, so declare one: " + string.Join(", ", unlisted));
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void The_list_only_shrinks(string service)
    {
        var list = CeilingList.Load(ListPath(service));
        var stale = CeilingChecks.Stale(Undeclared(service, WebAssemblyCatalog.Controllers(service)), list);
        Assert.True(stale.Count == 0, $"These controllers declare their stance now (or are gone): remove them from {ListPath(service)}: " + string.Join(", ", stale));
        var ceiling = Ceilings.GetValueOrDefault(service);
        Assert.True(list.Rows.Count <= ceiling, $"{ListPath(service)} holds {list.Rows.Count} rows, above its ceiling of {ceiling}");
        Assert.True(list.Malformed.Count == 0, $"{ListPath(service)}: " + string.Join("; ", list.Malformed));
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void A_latent_row_is_never_a_controller_a_restricted_user_reaches(string service)
    {
        var actual = OrgScopeReach.ActualControllers(service);
        var reached = CeilingList.Load(ListPath(service)).Rows
            .Where(r => r.Value.StartsWith(LatentOwner + ":", StringComparison.Ordinal) && actual.Contains(r.Key))
            .Select(r => r.Key)
            .ToList();
        Assert.True(reached.Count == 0,
            $"{service}: a restricted user reaches these controllers, so they are not latent and need a stance: " + string.Join(", ", reached));
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_id_of_a_scoped_action_has_a_key_or_is_declared_not_a_key(string service)
    {
        var offenders = UnkeyedIds(service, WebAssemblyCatalog.Controllers(service), RegistryOf(service)).ToList();
        Assert.True(offenders.Count == 0, $"{service}: " + string.Join("; ", offenders));
    }

    [Fact]
    public void A_service_with_scoped_controllers_builds_its_registry_from_its_own_Configure()
    {
        foreach (var entry in WebAssemblyCatalog.Entries)
        {
            var scoped = WebAssemblyCatalog.Controllers(entry.Service)
                .Any(c => ActionCatalog.ReadController(entry.Service, c).Any(a => OrgScopeActionStance.For(c, a.Method)?.Stance is OrgScopedAttribute));
            if (!scoped)
                continue;
            var registry = RegistryOf(entry.Service);
            Assert.True(registry is not null, $"{entry.Service} has [OrgScoped] controllers but no registry in {nameof(Configurations)}");
            Assert.NotEmpty(registry!.Keys);
            Assert.All(registry.Keys, kv => Assert.Equal(kv.Key, kv.Value.EntityType));
        }

        foreach (var (service, _) in Configurations)
            Assert.Equal(service, RegistryOf(service)!.Service);
    }

    [Fact]
    public void Each_latent_list_row_is_signed_by_the_latent_owner_with_a_reason()
    {
        foreach (var entry in WebAssemblyCatalog.Entries)
            foreach (var (key, reason) in CeilingList.Load(ListPath(entry.Service)).Rows)
                Assert.True(reason.StartsWith(LatentOwner + ":", StringComparison.Ordinal),
                    $"{ListPath(entry.Service)}: {key} | {reason}");
    }

    [Fact]
    public void The_reflection_reads_every_stance_type()
    {
        // The four stance attributes of ServiceDefaults implement the marker the tests read; [RecruitmentOrgScope] does too (HR.Core.Web).
        var stances = typeof(IOrgScopeStance).Assembly.GetTypes().Where(t => typeof(IOrgScopeStance).IsAssignableFrom(t) && !t.IsInterface).ToList();
        Assert.Contains(typeof(OrgScopedAttribute), stances);
        Assert.Contains(typeof(OrgScopeCentralOnlyAttribute), stances);
        Assert.Contains(typeof(OrgScopeNotApplicableAttribute), stances);
        Assert.All(stances, t => Assert.True(t.GetCustomAttribute<AttributeUsageAttribute>()?.ValidOn.HasFlag(AttributeTargets.Class) == true, t.Name));
    }
}
