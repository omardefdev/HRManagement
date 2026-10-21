namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-027 (F4): rejects ReserveListed applications whose reserve date has passed, across tenants
/// (see <see cref="ReserveListService.ExpireReserveAsync"/>). Idempotent.
/// </summary>
public sealed class RecruitmentReserveExpirer(ReserveListService reserveList)
{
    public Task<int> ExpireDueAsync(DateOnly todayUtc, CancellationToken ct) =>
        reserveList.ExpireReserveAsync(todayUtc, ct);
}
