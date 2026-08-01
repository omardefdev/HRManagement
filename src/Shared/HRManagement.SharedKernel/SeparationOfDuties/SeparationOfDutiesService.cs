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
    /// Returns the delegate user id if the principal has an active, non-revoked delegation covering the entity type.
    /// </summary>
    public Guid? ResolveDelegate(Guid principalId, string entityType, DateTime asOf)
    {
        var match = _delegations.FirstOrDefault(d =>
            !d.IsRevoked
            && d.PrincipalId == principalId
            && d.ActiveFrom <= asOf
            && d.ActiveUntil >= asOf
            && (d.EntityTypeScope is null
                || string.Equals(d.EntityTypeScope, entityType, StringComparison.OrdinalIgnoreCase)));

        return match?.DelegateId;
    }

    /// <summary>
    /// Returns whether <paramref name="actorId"/> may act for <paramref name="principalId"/> via active delegation.
    /// </summary>
    public bool IsActingAsDelegate(Guid actorId, Guid principalId, string entityType, DateTime asOf)
    {
        var resolved = ResolveDelegate(principalId, entityType, asOf);
        return resolved.HasValue && resolved.Value == actorId;
    }

    public static SodResult ValidateDelegation(ApprovalDelegation delegation)
    {
        if (delegation.PrincipalId == Guid.Empty || delegation.DelegateId == Guid.Empty)
            return SodResult.Deny("معرّف المفوِّض أو المفوَّض غير صالح.");

        if (delegation.PrincipalId == delegation.DelegateId)
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
