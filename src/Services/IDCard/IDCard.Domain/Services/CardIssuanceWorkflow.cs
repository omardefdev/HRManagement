using IDCard.Domain.Enums;

namespace IDCard.Domain.Services;

public static class CardIssuanceWorkflow
{
    public static bool CanProcess(CardRequestStatus status) =>
        status == CardRequestStatus.Pending;

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
