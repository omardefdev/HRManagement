using IDCard.Domain.Enums;

namespace IDCard.Domain.Services;

public static class CardIssuanceWorkflow
{
    /// <summary>PLAN-46: only a request someone other than its requester approved in the engine becomes a card.</summary>
    public static bool CanProcess(CardRequestStatus status) =>
        status == CardRequestStatus.Approved;

    /// <summary>A draft goes to the card office.</summary>
    public static bool CanSubmit(CardRequestStatus status) =>
        status == CardRequestStatus.Pending;

    /// <summary>The employee and the purpose change only in a draft.</summary>
    public static bool CanEdit(CardRequestStatus status) =>
        status == CardRequestStatus.Pending;

    public static bool CanWithdraw(CardRequestStatus status) =>
        status == CardRequestStatus.PendingApproval;

    public static bool CanCancel(CardRequestStatus status) =>
        status is CardRequestStatus.Pending or CardRequestStatus.PendingApproval or CardRequestStatus.Approved;

    public static bool CanRevoke(CardStatus status) =>
        status is CardStatus.Active or CardStatus.Lost;

    public static bool CanMarkLost(CardStatus status) =>
        status == CardStatus.Active;

    public static bool CanRenew(CardStatus status, DateOnly? expiresAt) =>
        status is CardStatus.Active or CardStatus.Expired or CardStatus.Lost;

    public static bool IsExpired(DateOnly? expiresAt) =>
        expiresAt.HasValue && expiresAt.Value < DateOnly.FromDateTime(DateTime.UtcNow);

    public static bool IsRevokeSlaBreached(DateTime? revokedAt, DateTime startedAt, int slaHours) =>
        !revokedAt.HasValue && DateTime.UtcNow > startedAt.AddHours(Math.Max(1, slaHours));
}
