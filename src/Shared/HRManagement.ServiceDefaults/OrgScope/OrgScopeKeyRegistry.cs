using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

/// <summary>The entity type of an id whose value is itself an org unit (a posted <c>OrgUnitId</c>): it is in scope when the unit is.</summary>
public sealed class OrgScopeUnit
{
    private OrgScopeUnit()
    {
    }
}

/// <summary>How an id name is read: the entity type it names (<see cref="EntityType"/>), or a declared non-key with its reason.</summary>
public sealed record OrgScopeIdRule(Type? EntityType, string? NotKeyReason)
{
    public bool IsKey => EntityType is not null;
}

/// <summary>
/// PLAN-52 (§1, P-4): the scope keys of one service. Each entity type maps to an expression of its org unit (or units, all of which must be in
/// the scope: a transfer's two sides): a direct column, a column reached through the employee (HR.Core's <c>Employee.PrimaryOrgUnitId</c>, the
/// local <c>EmployeeOrgUnitSnapshot</c> elsewhere), or through a parent entity. The expressions translate to SQL (SQLite and PostgreSQL); a
/// null unit, or a missing snapshot, is no key: the record is central (D-52-1). Each id name an action takes maps to an entity type, for the
/// whole service or for one controller, or is declared not a key with a reason. The filter and <c>WhereInOrgScope</c> read the scope only
/// (<see cref="OrgAccessScope"/>), never a role (D-IAM-3).
/// </summary>
public sealed class OrgScopeKeyRegistry(string service)
{
    private readonly Dictionary<Type, OrgScopeKey> _keys = new();
    private readonly Dictionary<string, OrgScopeIdRule> _ids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrgScopeIdRule> _controllerIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The service key, as <c>WebAssemblyCatalog</c> names it.</summary>
    public string Service { get; } = service;

    public IReadOnlyDictionary<Type, OrgScopeKey> Keys => _keys;

    /// <summary>The ids named for the whole service.</summary>
    public IReadOnlyDictionary<string, OrgScopeIdRule> Ids => _ids;

    /// <summary>An entity keyed by its <c>Id</c>; every unit expression must be in the scope.</summary>
    public OrgScopeKeyRegistry Key<TContext, TEntity>(params Func<TContext, Expression<Func<TEntity, Guid?>>>[] units)
        where TContext : DbContext
        where TEntity : class
        => KeyBy<TContext, TEntity>(_ => e => EF.Property<Guid>(e, "Id"), units);

    /// <summary>An entity keyed by another property (a snapshot keyed by <c>EmployeeId</c>).</summary>
    public OrgScopeKeyRegistry KeyBy<TContext, TEntity>(
        Func<TContext, Expression<Func<TEntity, Guid>>> id,
        params Func<TContext, Expression<Func<TEntity, Guid?>>>[] units)
        where TContext : DbContext
        where TEntity : class
    {
        if (units.Length == 0)
            throw new ArgumentException($"{typeof(TEntity).Name}: at least one unit expression is required.", nameof(units));
        if (!_keys.TryAdd(typeof(TEntity), new OrgScopeKey<TContext, TEntity>(id, units)))
            throw new InvalidOperationException($"{Service}: {typeof(TEntity).Name} has a scope key already.");
        return this;
    }

    /// <summary>The id <paramref name="name"/> names <paramref name="entityType"/> in every controller of the service.</summary>
    public OrgScopeKeyRegistry Id(string name, Type entityType) => Add(_ids, name, new OrgScopeIdRule(entityType, null));

    /// <summary>The id <paramref name="name"/> names no scoped record in any controller of the service.</summary>
    public OrgScopeKeyRegistry NotKey(string name, string reason) => Add(_ids, name, NotKeyRule(reason));

    public OrgScopeKeyRegistry Id<TController>(string name, Type entityType) =>
        Add(_controllerIds, ControllerKey(typeof(TController), name), new OrgScopeIdRule(entityType, null));

    public OrgScopeKeyRegistry NotKey<TController>(string name, string reason) =>
        Add(_controllerIds, ControllerKey(typeof(TController), name), NotKeyRule(reason));

    /// <summary>The rule of an id name in a controller: the controller's own, else the service's; null when neither names it.</summary>
    public OrgScopeIdRule? RuleFor(Type? controller, string name)
    {
        if (controller is not null && _controllerIds.TryGetValue(ControllerKey(controller, name), out var own))
            return own;
        return _ids.TryGetValue(name, out var rule) ? rule : null;
    }

    public bool HasKey(Type entityType) => entityType == typeof(OrgScopeUnit) || _keys.ContainsKey(entityType);

    public OrgScopeKey KeyOf(Type entityType) =>
        _keys.TryGetValue(entityType, out var key)
            ? key
            : throw new InvalidOperationException($"{Service}: no scope key for {entityType.Name} (register it in the service's OrgScopeKeys).");

    private OrgScopeKeyRegistry Add(Dictionary<string, OrgScopeIdRule> map, string key, OrgScopeIdRule rule)
    {
        if (!map.TryAdd(key, rule))
            throw new InvalidOperationException($"{Service}: the id {key} is named twice.");
        return this;
    }

    private static OrgScopeIdRule NotKeyRule(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("A reason is required.", nameof(reason))
            : new OrgScopeIdRule(null, reason);

    private static string ControllerKey(Type controller, string name) => $"{controller.FullName}|{name}";
}

/// <summary>Builds unit expressions that go through another record: an entity's employee, its parent.</summary>
public static class OrgScopeExpressions
{
    /// <summary>
    /// <c>x =&gt; unitOf(key(x))</c>: the unit of the record that <paramref name="key"/> names, e.g. <c>Through(r =&gt; r.EmployeeId, id =&gt;
    /// db.Employees.Where(e =&gt; (Guid?)e.Id == id).Select(e =&gt; e.PrimaryOrgUnitId).FirstOrDefault())</c>, which EF translates to a scalar
    /// subquery. A key with no row gives null: no key, central (D-52-1).
    /// </summary>
    public static Expression<Func<T, Guid?>> Through<T>(Expression<Func<T, Guid?>> key, Expression<Func<Guid?, Guid?>> unitOf) =>
        Expression.Lambda<Func<T, Guid?>>(new ParameterReplacer(unitOf.Parameters[0], key.Body).Visit(unitOf.Body)!, key.Parameters);

    internal sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

/// <summary>One entity's scope key: its id and its unit expressions, bound to a <see cref="DbContext"/> when a query uses them.</summary>
public abstract class OrgScopeKey
{
    public abstract Type EntityType { get; }

    public abstract Type ContextType { get; }

    public abstract int UnitCount { get; }

    /// <summary>True when the record exists (under the context's own filters) and every unit of it is in <paramref name="allowed"/>.</summary>
    public abstract Task<bool> IsInScopeAsync(DbContext db, Guid id, IReadOnlyCollection<Guid> allowed, CancellationToken ct = default);

    /// <summary>The record's units, as the key reads them (null for a missing unit); an empty list when the record does not exist.</summary>
    public abstract Task<IReadOnlyList<Guid?>> UnitsOfAsync(DbContext db, Guid id, CancellationToken ct = default);

    /// <summary>The predicate of <c>WhereInOrgScope</c>: every unit is set and in <paramref name="allowed"/>.</summary>
    public abstract LambdaExpression InScope(DbContext db, IReadOnlyCollection<Guid> allowed);
}

internal sealed class OrgScopeKey<TContext, TEntity>(
    Func<TContext, Expression<Func<TEntity, Guid>>> id,
    IReadOnlyList<Func<TContext, Expression<Func<TEntity, Guid?>>>> units) : OrgScopeKey
    where TContext : DbContext
    where TEntity : class
{
    private static readonly MethodInfo ContainsMethod = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(Guid));

    public override Type EntityType => typeof(TEntity);

    public override Type ContextType => typeof(TContext);

    public override int UnitCount => units.Count;

    public override Task<bool> IsInScopeAsync(DbContext db, Guid recordId, IReadOnlyCollection<Guid> allowed, CancellationToken ct = default) =>
        Set(db).Where(IdEquals(Typed(db), recordId)).Where(Predicate(Typed(db), allowed)).AnyAsync(ct);

    public override async Task<IReadOnlyList<Guid?>> UnitsOfAsync(DbContext db, Guid recordId, CancellationToken ct = default)
    {
        var context = Typed(db);
        var result = new List<Guid?>();
        foreach (var unit in units)
        {
            var rows = await Set(db).Where(IdEquals(context, recordId)).Select(unit(context)).Take(1).ToListAsync(ct);
            if (rows.Count == 0)
                return [];
            result.Add(rows[0]);
        }

        return result;
    }

    public override LambdaExpression InScope(DbContext db, IReadOnlyCollection<Guid> allowed) => Predicate(Typed(db), allowed);

    private static IQueryable<TEntity> Set(DbContext db) => db.Set<TEntity>().AsNoTracking();

    private static TContext Typed(DbContext db) =>
        db as TContext ?? throw new InvalidOperationException($"{typeof(TEntity).Name}'s scope key reads {typeof(TContext).Name}, not {db.GetType().Name}.");

    private Expression<Func<TEntity, bool>> IdEquals(TContext db, Guid recordId)
    {
        var selector = id(db);
        var holder = new IdHolder(recordId);
        var body = Expression.Equal(selector.Body, Expression.Property(Expression.Constant(holder), nameof(IdHolder.Value)));
        return Expression.Lambda<Func<TEntity, bool>>(body, selector.Parameters);
    }

    /// <summary><c>e =&gt; allowed.Contains(unit1(e) ?? Guid.Empty) &amp;&amp; …</c>: a null unit is never in the scope (Guid.Empty is no unit).
    /// The list is read through a holder, so EF parameterizes it and caches one query plan for every scope.</summary>
    private Expression<Func<TEntity, bool>> Predicate(TContext db, IReadOnlyCollection<Guid> allowed)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var holder = new AllowedHolder(allowed.ToList());
        var list = Expression.Property(Expression.Constant(holder), nameof(AllowedHolder.Values));
        Expression? body = null;
        foreach (var unit in units)
        {
            var expression = unit(db);
            var unitBody = new OrgScopeExpressions.ParameterReplacer(expression.Parameters[0], parameter).Visit(expression.Body)!;
            var test = Expression.Call(ContainsMethod, list, Expression.Coalesce(unitBody, Expression.Constant(Guid.Empty)));
            body = body is null ? test : Expression.AndAlso(body, test);
        }

        return Expression.Lambda<Func<TEntity, bool>>(body!, parameter);
    }

    private sealed class IdHolder(Guid value)
    {
        public Guid Value { get; } = value;
    }

    private sealed class AllowedHolder(List<Guid> values)
    {
        public List<Guid> Values { get; } = values;
    }
}
