namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>
/// Central Separation of Duties enforcement (IAM-003).
/// Validates maker/checker/poster actor separation, role conflicts, financial escalation, and delegation.
/// </summary>
public class SeparationOfDutiesService
{
    private readonly IReadOnlyList<SodRule> _rules;
    private readonly IReadOnlyList<ConflictingRolePair> _conflicts;
    private readonly IReadOnlyList<ApprovalDelegation> _delegations;

    public SeparationOfDutiesService(
        IEnumerable<SodRule>? rules = null,
        IEnumerable<ConflictingRolePair>? conflicts = null,
        IEnumerable<ApprovalDelegation>? delegations = null)
    {
        _rules = (rules ?? SodMatrixDefaults.CreateDefaultRules()).ToList();
        _conflicts = (conflicts ?? SodMatrixDefaults.CreateDefaultConflicts()).ToList();
        _delegations = (delegations ?? Array.Empty<ApprovalDelegation>()).ToList();
    }

    public SodRule? GetRule(string entityType) =>
        _rules.FirstOrDefault(r => string.Equals(r.EntityType, entityType, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the effective required approval count, escalating to four-eyes when amount exceeds threshold.
    /// </summary>
    public int GetRequiredApprovalCount(string entityType, decimal? financialAmount)
    {
        var rule = GetRule(entityType);
        if (rule is null)
            return 1;

        if (rule.FinancialThreshold.HasValue
            && financialAmount.HasValue
            && financialAmount.Value > rule.FinancialThreshold.Value)
        {
            return Math.Max(rule.RequiredApprovalCount, 2);
        }

        return Math.Max(1, rule.RequiredApprovalCount);
    }

    public bool RequiresDualApproval(string entityType, decimal? financialAmount = null) =>
        GetRequiredApprovalCount(entityType, financialAmount) >= 2;

    public SodResult ValidateAction(
        Guid actorId,
        string? actorRole,
        string entityType,
        Guid entityId,
        SodAction action,
        Guid? entityCreatorId,
        Guid? lastApproverId,
        decimal? financialAmount = null)
    {
        _ = entityId;
        var rule = GetRule(entityType);

        if (rule is not null && !string.IsNullOrWhiteSpace(actorRole))
        {
            var expectedRole = action switch
            {
                SodAction.Make => rule.MakerRole,
                SodAction.Check => rule.CheckerRole,
                SodAction.Post => rule.PosterRole ?? rule.CheckerRole,
                SodAction.Reverse => rule.ReverserRole ?? rule.PosterRole ?? rule.CheckerRole,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(expectedRole)
                && !string.Equals(actorRole, expectedRole, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(actorRole, "hr-admin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(actorRole, "platform-admin", StringComparison.OrdinalIgnoreCase))
            {
                // Soft role hint: ASP.NET policies remain the primary gate; SoD focuses on actor separation.
            }
        }

        var requireDifferent = rule?.RequireDifferentActors ?? true;

        if (requireDifferent && action == SodAction.Check)
        {
            if (entityCreatorId.HasValue && actorId == entityCreatorId.Value)
                return SodResult.Deny("لا يمكن للمنشئ اعتماد عمله الخاص.");
        }

        if (requireDifferent && action is SodAction.Post or SodAction.Reverse)
        {
            if (lastApproverId.HasValue && actorId == lastApproverId.Value)
                return SodResult.Deny("لا يمكن لنفس المستخدم اعتماد العملية ثم ترحيلها أو عكسها.");

            if (entityCreatorId.HasValue && actorId == entityCreatorId.Value)
                return SodResult.Deny("لا يمكن لمنشئ العملية ترحيلها أو عكسها.");
        }

        if (action == SodAction.Check
            && RequiresDualApproval(entityType, financialAmount)
            && lastApproverId.HasValue
            && actorId == lastApproverId.Value)
        {
            return SodResult.Deny("يتطلب الاعتماد المزدوج موافقة شخص ثانٍ مختلف.");
        }

        return SodResult.Ok();
    }

    public SodResult ValidateRoleAssignment(
        Guid targetUserId,
        IEnumerable<string> currentRoles,
        string newRole,
        Guid? assignorId = null)
    {
        if (assignorId.HasValue && assignorId.Value == targetUserId)
            return SodResult.Deny("لا يمكن للمستخدم منح دور لنفسه.");

        if (string.IsNullOrWhiteSpace(newRole))
            return SodResult.Deny("الدور المطلوب غير صالح.");

        var roles = currentRoles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var existing in roles)
        {
            if (RolesConflict(existing, newRole, out var reason))
                return SodResult.Deny(reason ?? "تعارض أدوار: لا يجوز الجمع بين هذين الدورين.");
        }

        return SodResult.Ok();
    }

    public bool RolesConflict(string roleA, string roleB, out string? reason)
    {
        reason = null;
        if (string.Equals(roleA, roleB, StringComparison.OrdinalIgnoreCase))
            return false;

        var pair = _conflicts.FirstOrDefault(c =>
            (string.Equals(c.RoleA, roleA, StringComparison.OrdinalIgnoreCase)
             && string.Equals(c.RoleB, roleB, StringComparison.OrdinalIgnoreCase))
            || (string.Equals(c.RoleA, roleB, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.RoleB, roleA, StringComparison.OrdinalIgnoreCase)));

        if (pair is null)
            return false;

        reason = pair.Reason;
        return true;
    }

    /// <summary>
    /// PLAN-50: the principal's delegation in force for the role and the entity type at the instant (its role scope respected), on
    /// <see cref="ApprovalDelegationMatcher"/>; null when there is none.
    /// </summary>
    public ApprovalDelegation? ResolveDelegate(Guid principalId, string role, string entityType, DateTime asOf) =>
        _delegations
            .Where(d => d.PrincipalId == principalId
                        && ApprovalDelegationMatcher.IsInForce(d, asOf)
                        && ApprovalDelegationMatcher.RoleMatches(d.RoleScope, role)
                        && ApprovalDelegationMatcher.EntityTypeMatches(d.EntityTypeScope, entityType))
            .OrderByDescending(d => d.ActiveUntil)
            .ThenBy(d => d.Id)
            .FirstOrDefault();

    /// <summary>
    /// PLAN-50: whether the actor (by employee, or by user for a row older than PLAN-50) acts for <paramref name="principalId"/> in the role on
    /// the entity type at the instant, on <see cref="ApprovalDelegationMatcher"/>.
    /// </summary>
    public bool IsActingAsDelegate(Guid? actorUserId, Guid? actorEmployeeId, Guid principalId, string role, string entityType, DateTime asOf) =>
        ApprovalDelegationMatcher.Match(
            _delegations.Where(d => d.PrincipalId == principalId), actorUserId, actorEmployeeId, role, entityType, asOf) is not null;

    /// <summary>
    /// PLAN-50 (D-WF-5, D-50-1): the full check at creation. The role is mandatory, delegable, and one the principal holds directly in his
    /// token (so a delegated role, which never reaches a token, cannot be delegated again); the entity type, when given, is one of the closed
    /// list; then <see cref="ValidateDelegation(ApprovalDelegation)"/>.
    /// </summary>
    public static SodResult ValidateDelegation(ApprovalDelegation delegation, DelegationRules rules)
    {
        if (string.IsNullOrWhiteSpace(delegation.RoleScope))
            return SodResult.Deny("يجب تحديد الدور المفوَّض.");

        var role = delegation.RoleScope.Trim();
        if (!rules.DelegableRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            return SodResult.Deny($"الدور «{role}» غير قابل للتفويض.");

        if (!rules.PrincipalDirectRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            return SodResult.Deny($"لا يمكن تفويض الدور «{role}» لأنك لا تحمله مباشرة.");

        if (!string.IsNullOrWhiteSpace(delegation.EntityTypeScope)
            && !rules.EntityTypes.Contains(delegation.EntityTypeScope.Trim(), StringComparer.OrdinalIgnoreCase))
            return SodResult.Deny($"نوع الكيان «{delegation.EntityTypeScope.Trim()}» ليس من الأنواع التي يجوز التفويض فيها.");

        return ValidateDelegation(delegation);
    }

    /// <summary>The identities, the self rule (by user and by employee), the period, the 90 days and the reason.</summary>
    public static SodResult ValidateDelegation(ApprovalDelegation delegation)
    {
        if (delegation.PrincipalId == Guid.Empty)
            return SodResult.Deny("معرّف المفوِّض غير صالح.");

        var hasDelegateEmployee = delegation.DelegateEmployeeId is Guid employee && employee != Guid.Empty;
        var hasDelegateUser = delegation.DelegateId is Guid user && user != Guid.Empty;
        if (!hasDelegateEmployee && !hasDelegateUser)
            return SodResult.Deny("يجب اختيار المفوَّض من دليل الموظفين.");

        if (delegation.DelegateId == delegation.PrincipalId
            || (hasDelegateEmployee && delegation.DelegateEmployeeId == delegation.PrincipalEmployeeId))
            return SodResult.Deny("لا يمكن التفويض للنفس.");

        if (delegation.ActiveUntil <= delegation.ActiveFrom)
            return SodResult.Deny("فترة التفويض غير صالحة.");

        var span = delegation.ActiveUntil - delegation.ActiveFrom;
        if (span.TotalDays > SodMatrixDefaults.MaxDelegationDays)
            return SodResult.Deny($"لا يجوز أن تتجاوز مدة التفويض {SodMatrixDefaults.MaxDelegationDays} يوماً.");

        if (string.IsNullOrWhiteSpace(delegation.Reason))
            return SodResult.Deny("سبب التفويض مطلوب.");

        return SodResult.Ok();
    }

    public IEnumerable<SodViolationReport> GenerateViolationReport(IEnumerable<UserRoleSnapshot> users)
    {
        var now = DateTime.UtcNow;
        foreach (var user in users)
        {
            var roles = user.Roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < roles.Count; i++)
            {
                for (var j = i + 1; j < roles.Count; j++)
                {
                    if (RolesConflict(roles[i], roles[j], out var reason))
                    {
                        yield return new SodViolationReport(
                            user.UserId,
                            user.UserName,
                            roles[i],
                            roles[j],
                            reason ?? "تعارض أدوار",
                            now);
                    }
                }
            }
        }
    }
}

/// <summary>
/// PLAN-50 (D-WF-5, D-50-1): what a delegation may name. <see cref="DelegableRoles"/> is <c>Delegation:DelegableRoles</c>,
/// <see cref="EntityTypes"/> is <c>Delegation:EntityTypes</c>, and <see cref="PrincipalDirectRoles"/> are the roles of the principal's own
/// token at creation.
/// </summary>
public sealed record DelegationRules(
    IReadOnlyCollection<string> DelegableRoles,
    IReadOnlyCollection<string> EntityTypes,
    IReadOnlyCollection<string> PrincipalDirectRoles);
