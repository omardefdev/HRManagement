using IDCard.Domain.Entities;
using IDCard.Domain.Enums;

namespace IDCard.Domain.Services;

public static class CardChecklistDefaults
{
    public const int DefaultRevokeSlaHours = 48;

    public static (string OwnerRole, int SlaHours, bool Required) GovernanceFor(CardChecklistStep step) => step switch
    {
        CardChecklistStep.Requested => ("idcard", 24, true),
        CardChecklistStep.Designed => ("idcard", 48, false),
        CardChecklistStep.Issued => ("idcard", 48, true),
        CardChecklistStep.Printed => ("idcard", 24, true),
        CardChecklistStep.Delivered => ("idcard", 24, true),
        CardChecklistStep.Revoked => ("idcard", DefaultRevokeSlaHours, true),
        _ => ("idcard", 48, false)
    };

    public static IEnumerable<CardChecklist> CreateFor(Guid correlationId) =>
        Enum.GetValues<CardChecklistStep>().Select(step =>
        {
            var (owner, sla, required) = GovernanceFor(step);
            return new CardChecklist
            {
                CorrelationId = correlationId,
                Step = step,
                IsRequired = required,
                OwnerRole = owner,
                SlaHours = sla
            };
        });

    public static void Mark(CardChecklist item, Guid? by, string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            throw new InvalidOperationException("EvidenceRef مطلوب.");
        item.IsCompleted = true;
        item.CompletedAt = DateTime.UtcNow;
        item.CompletedBy = by;
        item.EvidenceRef = evidence.Trim();
    }
}

public static class CardDeliveryRules
{
    public static bool HasDeliveryProof(string? signatureOrReceipt, Guid? receiptFileId)
    {
        if (receiptFileId.HasValue) return true;
        if (string.IsNullOrWhiteSpace(signatureOrReceipt)) return false;
        var s = signatureOrReceipt.Trim();
        if (string.Equals(s, "موقّع", StringComparison.Ordinal) ||
            string.Equals(s, "موقع", StringComparison.OrdinalIgnoreCase))
            return false;
        return s.Length >= 2;
    }
}

public static class CardRequestRules
{
    public static void SoftCancel(CardRequest request, string? reason)
    {
        request.IsCancelled = true;
        request.CancelledAt = DateTime.UtcNow;
        request.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء طلب البطاقة" : reason.Trim();
        if (request.Status == CardRequestStatus.Pending)
            request.Status = CardRequestStatus.Rejected;
    }
}
