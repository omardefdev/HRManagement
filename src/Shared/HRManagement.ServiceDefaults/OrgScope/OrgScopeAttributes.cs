namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-52 (§1, P-4): the scope stance a controller (or one action) declares. Exactly one applies to an action: its own, else its class's.
/// <c>OrgScopeCoverageTests</c> requires one on every controller of HR.Core, HR.Contracts and Admin.Logistics, and on every controller a
/// restricted user reaches anywhere; elsewhere a controller without one is a row of its service's org-scope list (latent, IAM-H).
/// </summary>
public interface IOrgScopeStance;

/// <summary>What a restricted user is told when an id of the action names a record outside his scope, or no scope key.</summary>
public enum OrgScopeAnswer
{
    /// <summary>D-52-5: the record does not exist for him (404), for details and for a POST by its id alike.</summary>
    NotFound,

    /// <summary>The answer an older test or the plan's §2 rule 2 keeps (403): employees, records, documents, AC-06's lookup, AC-32's
    /// assets, PLAN-34's lists, QA-034's hire wizard list.</summary>
    Forbid
}

/// <summary>
/// The controller's records belong to org units. <see cref="EntityType"/> is what its <c>id</c> names; every other id is resolved through the
/// service's <see cref="OrgScopeKeyRegistry"/>. For a restricted user, <see cref="OrgScopeActionFilter{TContext}"/> resolves every id of the
/// action before it runs: out of scope, or no key (D-52-1: a record with no unit is central) gives <see cref="OutOfScope"/>; a denied scope
/// gives 403. Lists, exports and pick-lists filter themselves with <c>WhereInOrgScope</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class OrgScopedAttribute : Attribute, IOrgScopeStance
{
    /// <summary>A controller whose actions take no <c>id</c> of their own (an export, a list page).</summary>
    public OrgScopedAttribute()
    {
    }

    public OrgScopedAttribute(Type entityType) => EntityType = entityType;

    public Type? EntityType { get; }

    public OrgScopeAnswer OutOfScope { get; set; } = OrgScopeAnswer.NotFound;
}

/// <summary>Central only: a restricted user is refused (403) on every action it covers, whatever its ids.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class OrgScopeCentralOnlyAttribute(string? reason = null) : Attribute, IOrgScopeStance
{
    public string? Reason { get; } = reason;
}

/// <summary>
/// The org scope does not apply: reference data everyone reads, self-service by ownership, a service-to-service endpoint, or a page with no
/// org records. The reason is required and is read by the coverage test.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class OrgScopeNotApplicableAttribute : Attribute, IOrgScopeStance
{
    public OrgScopeNotApplicableAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.", nameof(reason));
        Reason = reason;
    }

    public string Reason { get; }
}

/// <summary>
/// The entity type an id names, where the registry's mapping by name does not fit: on a parameter or a bound model's property, that id; on an
/// action, its <c>id</c> (or the parameter named by <see cref="Name"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = true)]
public sealed class OrgScopeIdAttribute(Type entityType) : Attribute
{
    public Type EntityType { get; } = entityType;

    /// <summary>On an action: the parameter it applies to (default <c>id</c>).</summary>
    public string? Name { get; set; }
}

/// <summary>An id that names no scoped record (a contract type, a leave type, a catalogue entry, a file of a record already checked).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = true)]
public sealed class NotOrgScopeKeyAttribute : Attribute
{
    public NotOrgScopeKeyAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.", nameof(reason));
        Reason = reason;
    }

    public string Reason { get; }

    /// <summary>On an action: the parameter it applies to.</summary>
    public string? Name { get; set; }
}
