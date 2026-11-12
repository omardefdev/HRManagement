using HRManagement.SharedKernel.Entities;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// Controller actions must not model-bind persistence entities. <see cref="BaseEntity"/> exposes <c>Id</c>, <c>TenantId</c>,
/// <c>CreatedAt</c> and <c>UpdatedAt</c> as public settable properties, and entities add their own privileged fields on top (approval
/// status, approver identity, leave entitlement, monetary amounts, role permissions): binding the entity hands all of them to the caller.
/// An action that accepts data takes a view model carrying only the fields a user may supply.
/// <para>
/// PLAN-51 (§6, P-8) moved this test here from the unit project, where it scanned the four web assemblies that project referenced and
/// skipped the others silently. It now reads all fifteen. A binding that remains is a row of
/// <c>tests/architecture/entity-binding-allowlist/{service}.txt</c>, a list that only shrinks, signed by the plan that owns it.
/// </para>
/// </summary>
public sealed class MassAssignmentArchitectureTests
{
    public static string ListPath(string service) => $"tests/architecture/entity-binding-allowlist/{service}.txt";

    /// <summary>The most rows each list may hold. Lower a ceiling when its list shrinks; never raise one.</summary>
    public static readonly IReadOnlyDictionary<string, int> Ceilings = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        // PLAN-51 (P-8): HR.Movements binds view models; the rest is wave 2 (IAM-F, G-IAM-10).
        ["hr-movements"] = 0,
        ["hr-health"] = 20,
        ["payroll"] = 16,
        ["hr-training"] = 14,
        ["it-infra"] = 14,
        ["dev-analytics"] = 12,
        ["records-archive"] = 12,
        ["compliance"] = 11,
        ["idcard"] = 10,
        ["auth-directory"] = 4,
    };

    public static IEnumerable<string> Offenders(IEnumerable<ActionInfo> actions) =>
        actions.Where(a => a.Method.GetParameters().Any(p => BindsEntity(p.ParameterType, depth: 1)))
            .Select(a => a.Key)
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// PLAN-51 (review L3): an entity, an array or a collection of entities, or a bound model with a public settable property of one of
    /// those (one level down, as the model binder fills it).
    /// </summary>
    public static bool BindsEntity(Type type, int depth)
    {
        if (IsEntityOrEntities(type))
            return true;
        if (depth <= 0 || !type.IsClass || type == typeof(string) || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("Microsoft", StringComparison.Ordinal) == true)
            return false;
        // A [BindNever] property (a display list on a filter model) is never bound, as the model binder itself decides.
        return type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod?.IsPublic == true
                        && !p.IsDefined(typeof(Microsoft.AspNetCore.Mvc.ModelBinding.BindNeverAttribute), inherit: true))
            .Any(p => IsEntityOrEntities(p.PropertyType));
    }

    private static bool IsEntityOrEntities(Type type)
    {
        if (typeof(BaseEntity).IsAssignableFrom(type))
            return true;
        if (type.IsArray)
            return typeof(BaseEntity).IsAssignableFrom(type.GetElementType()!);
        return type.IsGenericType
               && type.GetGenericArguments().Any(t => typeof(BaseEntity).IsAssignableFrom(t))
               && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    public static TheoryData<string> Services() => new(WebAssemblyCatalog.Entries.Select(e => e.Service));

    [Theory]
    [MemberData(nameof(Services))]
    public void No_controller_action_binds_a_persistence_entity(string service)
    {
        var unlisted = CeilingChecks.Unlisted(Offenders(ActionCatalog.Of(service)), CeilingList.Load(ListPath(service)));
        Assert.True(unlisted.Count == 0,
            $"{service}: controller actions must bind a view model, not a persistence entity (Id, TenantId, CreatedAt and the entity's own "
            + "privileged fields all become client-settable once bound): " + string.Join(", ", unlisted));
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void The_list_only_shrinks(string service)
    {
        var list = CeilingList.Load(ListPath(service));
        var stale = CeilingChecks.Stale(Offenders(ActionCatalog.Of(service)), list);
        Assert.True(stale.Count == 0, $"These actions bind a view model now: remove them from {ListPath(service)}: " + string.Join(", ", stale));
        var ceiling = Ceilings.GetValueOrDefault(service);
        Assert.True(list.Rows.Count <= ceiling, $"{ListPath(service)} holds {list.Rows.Count} rows, above its ceiling of {ceiling}");
        Assert.True(list.Malformed.Count == 0, $"{ListPath(service)}: " + string.Join("; ", list.Malformed));
    }
}
