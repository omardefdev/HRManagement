using HR.Health.Domain.Entities;
using HR.Health.Domain.Enums;

namespace HR.Health.Domain.Services;

public static class InsuranceLifecycleRules
{
    public static bool CanActivate(InsuranceCard card) =>
        !card.IsCancelled && card.Status is InsuranceCardStatus.Draft or InsuranceCardStatus.Suspended;

    public static bool CanSuspend(InsuranceCard card) =>
        !card.IsCancelled && card.Status == InsuranceCardStatus.Active;

    public static bool CanRevoke(InsuranceCard card) =>
        !card.IsCancelled && card.Status is InsuranceCardStatus.Active or InsuranceCardStatus.Suspended or InsuranceCardStatus.Draft;

    public static bool CanMarkExpired(InsuranceCard card, DateOnly? today = null)
    {
        if (card.IsCancelled) return false;
        var d = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return card.Status == InsuranceCardStatus.Active && card.ExpiryDate < d;
    }

    public static void SoftCancel(InsuranceCard card, string? reason)
    {
        card.IsCancelled = true;
        card.CancelledAt = DateTime.UtcNow;
        card.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء بطاقة التأمين" : reason.Trim();
        if (card.Status is not InsuranceCardStatus.Revoked and not InsuranceCardStatus.Expired)
            card.Status = InsuranceCardStatus.Revoked;
    }
}
