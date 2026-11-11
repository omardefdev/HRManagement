namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>
/// PLAN-50 (§5): the one rule that says whether a delegation acts. Pure: no clock, no database, no claims. A delegation acts for a role
/// and an entity type at an instant when it is not revoked, has a role scope equal to the role (case-insensitive), names that entity
/// type or none, and the instant lies within its period; and it acts for an actor whose employee id is its delegate employee, or, for a
/// row older than PLAN-50 that names no employee, whose user id is its delegate user (D-50-2). When several match, the one that ends
/// last wins (ties: the lowest id), so the result does not depend on the order of the input.
/// </summary>
public static class ApprovalDelegationMatcher
{
    /// <summary>In force at <paramref name="asOf"/>: not revoked, a role scope, the instant within the period.</summary>
    public static bool IsInForce(ApprovalDelegation delegation, DateTime asOf) =>
        !delegation.IsRevoked
        && !string.IsNullOrWhiteSpace(delegation.RoleScope)
        && delegation.ActiveFrom <= asOf
        && asOf <= delegation.ActiveUntil;

    /// <summary>The actor is this delegation's delegate: by employee, or by user for a row that names no employee.</summary>
    public static bool IsDelegate(ApprovalDelegation delegation, Guid? actorUserId, Guid? actorEmployeeId)
    {
        if (delegation.DelegateEmployeeId is Guid employee && employee != Guid.Empty)
            return actorEmployeeId == employee;
        return delegation.DelegateId is Guid user && user != Guid.Empty && actorUserId == user;
    }

    public static bool RoleMatches(string? roleScope, string role) =>
        !string.IsNullOrWhiteSpace(roleScope)
        && !string.IsNullOrWhiteSpace(role)
        && string.Equals(roleScope.Trim(), role.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A delegation with no entity scope covers every type; one with a scope covers that type only (and none when the type is unknown).</summary>
    public static bool EntityTypeMatches(string? entityTypeScope, string? entityType) =>
        string.IsNullOrWhiteSpace(entityTypeScope)
        || (!string.IsNullOrWhiteSpace(entityType)
            && string.Equals(entityTypeScope.Trim(), entityType.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The delegation through which this actor acts for the role and entity type at the instant, or null.</summary>
    public static ApprovalDelegation? Match(
        IEnumerable<ApprovalDelegation> delegations,
        Guid? actorUserId,
        Guid? actorEmployeeId,
        string role,
        string? entityType,
        DateTime asOf) =>
        delegations
            .Where(d => IsInForce(d, asOf)
                        && RoleMatches(d.RoleScope, role)
                        && EntityTypeMatches(d.EntityTypeScope, entityType)
                        && IsDelegate(d, actorUserId, actorEmployeeId))
            .OrderByDescending(d => d.ActiveUntil)
            .ThenBy(d => d.Id)
            .FirstOrDefault();

    /// <summary>
    /// The same rule over the delegations a lookup returned for one actor (the lookup matched the delegate): the role, the entity type and
    /// the end of the period, which a cached answer may have passed.
    /// </summary>
    public static ActiveDelegation? Match(
        IEnumerable<ActiveDelegation> delegations,
        string role,
        string? entityType,
        DateTime asOf) =>
        delegations
            .Where(d => RoleMatches(d.Role, role)
                        && EntityTypeMatches(d.EntityTypeScope, entityType)
                        && asOf <= d.ActiveUntil)
            .OrderByDescending(d => d.ActiveUntil)
            .ThenBy(d => d.Id)
            .FirstOrDefault();

    /// <summary>
    /// For a coarse route check that does not know the entity type (the engine's step policy): a delegation for the role, of any entity
    /// scope, still in force. The decision point then applies <see cref="Match(IEnumerable{ActiveDelegation}, string, string?, DateTime)"/>.
    /// </summary>
    public static ActiveDelegation? MatchRole(IEnumerable<ActiveDelegation> delegations, string role, DateTime asOf) =>
        delegations
            .Where(d => RoleMatches(d.Role, role) && asOf <= d.ActiveUntil)
            .OrderByDescending(d => d.ActiveUntil)
            .ThenBy(d => d.Id)
            .FirstOrDefault();

    /// <summary>Every delegation in force at the instant whose delegate is the actor, as the lookups return them.</summary>
    public static IReadOnlyList<ActiveDelegation> ActiveFor(
        IEnumerable<ApprovalDelegation> delegations,
        Guid? actorUserId,
        Guid? actorEmployeeId,
        DateTime asOf) =>
        delegations
            .Where(d => IsInForce(d, asOf) && IsDelegate(d, actorUserId, actorEmployeeId))
            .OrderByDescending(d => d.ActiveUntil)
            .ThenBy(d => d.Id)
            .Select(ToActive)
            .ToList();

    public static ActiveDelegation ToActive(ApprovalDelegation d) =>
        new(d.Id, d.PrincipalId, d.PrincipalEmployeeId, d.PrincipalName, d.RoleScope!.Trim(), d.EntityTypeScope, d.ActiveUntil,
            d.PrincipalScopeOrgUnitId);
}
