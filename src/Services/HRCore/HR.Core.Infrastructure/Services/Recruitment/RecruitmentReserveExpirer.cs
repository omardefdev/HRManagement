namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-027 (F4): rejects ReserveListed applications whose reserve date has passed, across tenants
/// (see <see cref="ReserveListService.ExpireReserveAsync"/>). Idempotent.
/// </summary>
public sealed class RecruitmentReserveExpirer(
    ReserveListService reserveList,
    Competitions.CompetitionReserveService? competitionReserve = null)
{
    public async Task<int> ExpireDueAsync(DateOnly todayUtc, CancellationToken ct)
    {
        // HCM-031 (PLAN-31): competition line reserves expire first (their applications are excluded below).
        var competitionEntries = competitionReserve is null ? 0 : await competitionReserve.ExpireAsync(todayUtc, ct);
        return competitionEntries + await reserveList.ExpireReserveAsync(todayUtc, ct);
    }
}
