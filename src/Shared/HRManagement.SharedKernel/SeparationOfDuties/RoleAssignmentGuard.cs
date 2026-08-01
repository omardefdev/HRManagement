namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>
/// Guards role grants against self-escalation and conflicting role pairs (IAM-003).
/// </summary>
public static class RoleAssignmentGuard
{
    /// <summary>Role rank used to prevent granting a role higher than the assignor's highest role.</summary>
    private static readonly Dictionary<string, int> RoleRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hr-employee"] = 10,
        ["branch-manager"] = 20,
        ["hr-core"] = 30,
        ["hr-core-supervisory"] = 30,
        ["hr-core-executive"] = 30,
        ["hr-contracts"] = 30,
        ["hr-health"] = 30,
        ["hr-movements"] = 30,
        ["hr-training"] = 30,
        ["payroll"] = 30,
        ["payroll-processor"] = 35,
        ["payroll-approver"] = 40,
        ["payroll-poster"] = 45,
        ["hr-management-approver"] = 50,
        ["auth-directory"] = 55,
        ["compliance"] = 55,
        ["hr-admin"] = 90,
        ["platform-admin"] = 100
    };

    public static SodResult ValidateGrant(
        Guid assignorId,
        IEnumerable<string> assignorRoles,
        Guid targetUserId,
        IEnumerable<string> targetCurrentRoles,
        string newRole,
        SeparationOfDutiesService? sod = null)
    {
        sod ??= new SeparationOfDutiesService();

        if (assignorId == targetUserId)
            return SodResult.Deny("لا يمكن للمستخدم منح دور لنفسه.");

        var assignment = sod.ValidateRoleAssignment(targetUserId, targetCurrentRoles, newRole, assignorId);
        if (!assignment.Allowed)
            return assignment;

        var assignorRank = MaxRank(assignorRoles);
        var newRank = RankOf(newRole);
        if (newRank > assignorRank)
            return SodResult.Deny("لا يمكن منح دور أعلى من صلاحيات المانح.");

        if (IsPrivileged(newRole))
            return SodResult.Deny("منح الأدوار الحساسة يتطلب اعتماداً مزدوجاً (four-eyes).");

        return SodResult.Ok();
    }

    /// <summary>
    /// Validates a privileged role grant that already has a second approver.
    /// </summary>
    public static SodResult ValidatePrivilegedGrant(
        Guid assignorId,
        Guid secondApproverId,
        Guid targetUserId,
        IEnumerable<string> targetCurrentRoles,
        string newRole,
        SeparationOfDutiesService? sod = null)
    {
        sod ??= new SeparationOfDutiesService();

        if (assignorId == targetUserId || secondApproverId == targetUserId)
            return SodResult.Deny("لا يمكن للمستخدم منح دور لنفسه.");

        if (assignorId == secondApproverId)
            return SodResult.Deny("يتطلب منح الدور الحساس موافقة شخصين مختلفين.");

        return sod.ValidateRoleAssignment(targetUserId, targetCurrentRoles, newRole, assignorId);
    }

    public static bool IsPrivileged(string role) =>
        SodMatrixDefaults.PrivilegedRoles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    public static bool RequiresDualApprovalForRole(string role) => IsPrivileged(role);

    private static int RankOf(string role) =>
        RoleRank.TryGetValue(role, out var rank) ? rank : 25;

    private static int MaxRank(IEnumerable<string> roles) =>
        roles.Select(RankOf).DefaultIfEmpty(0).Max();
}
