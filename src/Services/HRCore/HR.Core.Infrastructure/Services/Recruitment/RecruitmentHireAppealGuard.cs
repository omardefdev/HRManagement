using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-028 (I): no hire on a requisition while any of its applications has a pending appeal
/// (<c>Recruitment:BlockHireOnPendingAppeal</c>, default true).
/// </summary>
public static class RecruitmentHireAppealGuard
{
    public static async Task<string?> ValidateAsync(
        HRCoreDbContext db,
        Guid? requisitionId,
        bool blockHireOnPendingAppeal,
        CancellationToken ct = default)
    {
        if (!blockHireOnPendingAppeal || requisitionId is null)
            return null;

        var pending = await db.RecruitmentApplications.AnyAsync(
            a => a.RequisitionId == requisitionId && a.AppealStatus == RecruitmentAppealStatus.Pending, ct);
        return pending ? RecruitmentAppealWorkflowRules.HireBlockedMessage : null;
    }
}
