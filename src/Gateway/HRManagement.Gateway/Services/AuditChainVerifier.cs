using HRManagement.Gateway.Data;
using HRManagement.Gateway.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.Gateway.Services;

public interface IAuditChainVerifier
{
    Task<AuditChainVerifyResult> VerifyAsync(CancellationToken ct = default);
}

public sealed record AuditChainVerifyResult(bool Ok, int Checked, string? Error);

public sealed class AuditChainVerifier(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditChainVerifier> logger) : IAuditChainVerifier
{
    public async Task<AuditChainVerifyResult> VerifyAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
        var rows = await db.AuditEntries.AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        // Prefer default chain; include null/empty ChainKey for pre-migration rows.
        rows = rows
            .Where(x => string.IsNullOrEmpty(x.ChainKey)
                        || x.ChainKey == AuditHashChain.DefaultChainKey)
            .ToList();

        if (rows.Count == 0)
            return new AuditChainVerifyResult(true, 0, null);

        string? prev = AuditHashChain.GenesisPrevHash;
        DateTimeOffset? lastTs = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (string.IsNullOrEmpty(row.EventHash))
            {
                // Pre-OBS-003 rows: skip until first sealed row
                if (i == 0 || rows.Take(i).All(r => string.IsNullOrEmpty(r.EventHash)))
                    continue;
                return new AuditChainVerifyResult(false, i, $"Empty EventHash at Id={row.Id}");
            }

            if (prev is not null && row.PrevHash != prev && !(prev == AuditHashChain.GenesisPrevHash && i == 0))
            {
                // Allow genesis only for first sealed row
                var firstSealed = rows.FindIndex(r => !string.IsNullOrEmpty(r.EventHash));
                if (!(i == firstSealed && row.PrevHash == AuditHashChain.GenesisPrevHash))
                {
                    logger.LogError("Chain break at Id={Id}: PrevHash mismatch", row.Id);
                    return new AuditChainVerifyResult(false, i, $"PrevHash mismatch at Id={row.Id}");
                }
            }

            if (!AuditHashChain.Verify(row))
            {
                logger.LogError("Chain break at Id={Id}: EventHash mismatch", row.Id);
                return new AuditChainVerifyResult(false, i, $"EventHash mismatch at Id={row.Id}");
            }

            if (lastTs.HasValue && row.OccurredAtUtc < lastTs.Value.AddMinutes(-5))
            {
                logger.LogWarning("Suspicious timestamp regression at Id={Id}", row.Id);
            }

            prev = row.EventHash;
            lastTs = row.OccurredAtUtc;
        }

        return new AuditChainVerifyResult(true, rows.Count(r => !string.IsNullOrEmpty(r.EventHash)), null);
    }
}
