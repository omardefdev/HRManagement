using HRManagement.SharedKernel.Evidence;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults.Evidence;

/// <summary>
/// Appends sealed evidence rows. Requires a DbContext that includes <see cref="EvidenceRecord"/>.
/// </summary>
public sealed class EvidenceCollector(DbContext db) : IEvidenceCollector
{
    public async Task<EvidenceRecord> RecordAsync(EvidenceWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (db.Model.FindEntityType(typeof(EvidenceRecord)) is null)
            throw new InvalidOperationException("DbContext is not configured with EvidenceRecords (EVD-001).");

        var set = db.Set<EvidenceRecord>();
        var last = await set.AsNoTracking()
            .Where(x => x.ChainKey == request.ChainKey)
            .OrderByDescending(x => x.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        var seq = (last?.Sequence ?? 0) + 1;
        var prev = last?.EventHash ?? EvidenceHashChain.GenesisPrevHash;

        var row = new EvidenceRecord
        {
            TenantId = request.TenantId,
            CorrelationId = request.CorrelationId,
            CausationId = request.CausationId,
            Actor = request.Actor,
            Subject = request.Subject,
            Resource = request.Resource,
            Purpose = request.Purpose,
            AssuranceLevel = request.AssuranceLevel,
            Policy = request.Policy,
            Rule = request.Rule,
            Reason = request.Reason,
            Result = request.Result,
            BeforeHash = request.BeforeHash,
            AfterHash = request.AfterHash,
            EvidenceLinksJson = request.EvidenceLinksJson,
            ReleaseVersion = request.ReleaseVersion,
            ConfigVersion = request.ConfigVersion,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            TimeSource = request.TimeSource,
            TimeZoneId = request.TimeZoneId,
            ChainKey = request.ChainKey,
            Sequence = seq
        };
        EvidenceHashChain.Seal(row, prev);

        set.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }
}
