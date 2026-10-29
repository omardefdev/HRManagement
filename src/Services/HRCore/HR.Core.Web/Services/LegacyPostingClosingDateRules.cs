using System.Text.Json;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Web.Services;

/// <summary>
/// PLAN-25 (G-15): closing-date changes on legacy (not requisition-linked) public postings and internal vacancies are
/// audited, and a posting that already has applications cannot be closed retroactively (moved before today).
/// Requisition-linked ones are managed on the requisition (PLAN-26).
/// </summary>
public static class LegacyPostingClosingDateRules
{
    public const string AuditAction = "ClosingDateChanged";
    public const string ShortenedBelowTodayMessage =
        "لا يمكن تقديم تاريخ إغلاق التقديم إلى ما قبل اليوم لإعلان وصلته طلبات.";

    public static string? Validate(DateOnly? current, DateOnly? requested, bool hasApplications, DateOnly today) =>
        requested != current && hasApplications && requested is DateOnly newDate && newDate < today
            ? ShortenedBelowTodayMessage
            : null;

    public static void AuditIfChanged(
        HRCoreDbContext db, string entityType, Guid entityId, DateOnly? before, DateOnly? after, Guid? actorUserId)
    {
        if (before == after)
            return;
        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = AuditAction,
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new
            {
                before = before?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                after = after?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            })
        });
    }
}
