using Compliance.Domain.Entities;
using Compliance.Domain.Enums;

namespace Compliance.Domain.Services;

public static class InvestigationChecklistDefaults
{
    public static (string OwnerRole, int SlaHours, bool Required) GovernanceFor(InvestigationChecklistStep step) => step switch
    {
        InvestigationChecklistStep.Assigned => ("compliance", 48, true),
        InvestigationChecklistStep.EvidenceCollected => ("compliance", 168, true),
        InvestigationChecklistStep.Decision => ("compliance", 72, true),
        InvestigationChecklistStep.Closed => ("compliance", 24, true),
        _ => ("compliance", 48, false)
    };

    public static IEnumerable<InvestigationChecklist> CreateFor(Guid complaintId) =>
        Enum.GetValues<InvestigationChecklistStep>().Select(step =>
        {
            var (owner, sla, required) = GovernanceFor(step);
            return new InvestigationChecklist
            {
                ComplaintId = complaintId,
                Step = step,
                IsRequired = required,
                OwnerRole = owner,
                SlaHours = sla
            };
        });

    public static void Mark(InvestigationChecklist item, Guid? by, string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            throw new InvalidOperationException("EvidenceRef مطلوب.");
        item.IsCompleted = true;
        item.CompletedAt = DateTime.UtcNow;
        item.CompletedBy = by;
        item.EvidenceRef = evidence.Trim();
    }
}

public static class ComplaintProtectionRules
{
    public static bool CanSeeReporter(Complaint complaint, Guid? viewerUserId, bool isInvestigatorRole)
    {
        if (!complaint.IsAnonymous) return true;
        if (!isInvestigatorRole) return false;
        if (complaint.AssignedTo.HasValue && viewerUserId.HasValue && complaint.AssignedTo == viewerUserId)
            return true;
        // Investigators with role but unassigned: still allow for case management after assign gate
        return isInvestigatorRole && complaint.AssignedTo.HasValue;
    }

    public static bool CanAssignInvestigator(Complaint complaint, Guid investigatorId)
    {
        if (complaint.ReporterProtectedId.HasValue && complaint.ReporterProtectedId == investigatorId)
            return false;
        return true;
    }

    public static void SoftCancel(Complaint complaint, string? reason)
    {
        complaint.IsCancelled = true;
        complaint.CancelledAt = DateTime.UtcNow;
        complaint.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء الشكوى" : reason.Trim();
    }
}
